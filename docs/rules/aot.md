---
id: rules-aot
title: Native AOT Rules (ZA17xx)
slug: /docs/rules/aot
description: Native AOT and trimming compatibility rules for reflection and runtime code generation.
sidebar_position: 17
---

# Native AOT (ZA17xx)

Native AOT compiles your app to native code ahead of time, with no JIT at runtime. That delivers fast startup and small, self-contained binaries — but anything that **generates code or resolves members by name at runtime** stops working. The ZA17xx rules flag the highest-signal "this cannot work under Native AOT" patterns.

## Relationship to the SDK's own AOT analyzer

When a project sets `<PublishAot>true</PublishAot>` or `<IsAotCompatible>true</IsAotCompatible>`, the .NET SDK enables the official AOT analyzer (`IL3050`+) and trimming analyzer (`IL2xxx`), which authoritatively cover these patterns. To avoid double-reporting, **every ZA17xx rule automatically stands down when that opt-in is detected** (via the `PublishAot`, `IsAotCompatible`, or `EnableAotAnalyzer` MSBuild properties). The one exception is ZA1710, which stands down per call and only for scanning APIs that are trim- or AOT-annotated; see [ZA1710](#za1710).

The value ZA17xx adds is for the **common case where you have not opted in yet** — for example a library that has not set `IsAotCompatible`. There the SDK analyzers are silent, and these rules surface AOT hazards early so the eventual move to AOT is smaller.

Several existing ZeroAlloc rules already steer toward AOT-friendly code: [ZA1001](serialization.md#za1001) (JSON source generation), [ZA0701](regex.md#za0701) (GeneratedRegex), and [ZA0401](logging.md#za0401) (LoggerMessage).

ZA1706 to ZA1709 cover the next layer: a library that has not switched the SDK analyzers on at all (ZA1706), and the reflection-based helpers in `Microsoft.Extensions` that have a source-generated replacement (ZA1707 and ZA1708), plus `dynamic` (ZA1709). ZA1710 reports registration that scans assemblies with reflection.

---

## ZA1701 — Avoid compiling expression trees at runtime {#za1701}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`Expression<T>.Compile()` (and `LambdaExpression.Compile()`) turns an expression tree into a delegate by emitting IL at runtime. Native AOT has no runtime IL generator, so the call throws. Where a hot path uses a compiled expression as a fast accessor, precompute it differently — a source generator, a hand-written delegate, or `Compile(preferInterpretation: true)` only as a fallback.

```csharp
// ❌ runtime IL generation — fails under Native AOT
Expression<Func<Order, decimal>> expr = o => o.Total;
Func<Order, decimal> getTotal = expr.Compile();

// ✓ a plain delegate needs no codegen
Func<Order, decimal> getTotal = static o => o.Total;
```

---

## ZA1702 — Avoid runtime IL generation {#za1702}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

Types in `System.Reflection.Emit` — `DynamicMethod`, `AssemblyBuilder`, `ILGenerator`, `TypeBuilder`, and friends — exist solely to emit IL at runtime, which Native AOT does not support. Replace dynamic dispatch tables and generated accessors with source generators or static code.

```csharp
// ❌ emits a method at runtime — fails under Native AOT
var dm = new DynamicMethod("Add", typeof(int), new[] { typeof(int), typeof(int) });
var il = dm.GetILGenerator();
// ...
```

---

## ZA1703 — Avoid constructing generic types or methods at runtime {#za1703}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`Type.MakeGenericType(...)` and `MethodInfo.MakeGenericMethod(...)` instantiate a generic over types only known at runtime. The AOT compiler cannot know which instantiations to emit, so it requires dynamic code and the call fails. Prefer generic methods/types resolved at compile time, or a closed set of explicitly-referenced instantiations.

```csharp
// ❌ runtime generic instantiation — requires dynamic code
var listType = typeof(List<>).MakeGenericType(elementType);
var list = Activator.CreateInstance(listType);

// ✓ compile-time generic — fully AOT-safe
var list = new List<int>();
```

---

## ZA1704 — Avoid reflection-based serializers {#za1704}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`XmlSerializer`, `DataContractSerializer`, `DataContractJsonSerializer`, and `BinaryFormatter` discover members by reflection and (for XML) generate serialization assemblies at runtime — neither trim- nor AOT-safe, and the members they read can be removed by the trimmer. Prefer a source-generated serializer. For JSON, see [ZA1001](serialization.md#za1001) and use `System.Text.Json` source generation.

```csharp
// ❌ reflection-based — not trim/AOT-safe
var serializer = new XmlSerializer(typeof(Order));

// ✓ source-generated JSON (System.Text.Json)
[JsonSerializable(typeof(Order))]
partial class AppJsonContext : JsonSerializerContext { }

JsonSerializer.Serialize(order, AppJsonContext.Default.Order);
```

---

## ZA1705 — Avoid resolving types or assemblies by name {#za1705}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No | **Enabled by default**: No (opt-in)

### Why

`Type.GetType(string)`, `Assembly.Load(string)`, `Assembly.LoadFrom/LoadFile`, and similar resolve code by name at runtime. The trimmer cannot see which types that keeps alive, so they may be trimmed away, and AOT cannot follow the indirection. (Note: the *instance* `object.GetType()` is the safe runtime-type query and is **not** flagged.)

Because name-based loading is sometimes deliberate — plugin hosts, extensibility points — this rule is **disabled by default**. Enable it on code you intend to make AOT-clean:

```ini
# .editorconfig
dotnet_diagnostic.ZA1705.severity = info
```

```csharp
// ❌ the trimmer/AOT cannot follow this
var t = Type.GetType("MyApp.Plugins.AcmePlugin, MyApp.Plugins");

// ✓ reference the type directly so it stays rooted
var t = typeof(AcmePlugin);
```

### Suppression

```csharp
#pragma warning disable ZA1705
// or in .editorconfig:
// dotnet_diagnostic.ZA1705.severity = none   (already disabled by default)
```

---

## ZA1706 — Mark libraries as AOT-compatible {#za1706}

> **Severity**: Info | **Min TFM**: net8.0 | **Code fix**: No

### Why

The SDK's trim and AOT analyzers (`IL2xxx`, `IL3xxx`) only run when a project sets `IsAotCompatible`, `PublishAot` or `EnableAotAnalyzer`. A library that sets none of them can ship code that breaks under Native AOT, and nothing warns until an app publishes with it. Setting `IsAotCompatible` turns those analyzers on for the library itself, so AOT problems surface where they can be fixed.

The rule reports once per project, with no source location, for a net8.0 or later library that has never set `IsAotCompatible`. It stays silent for apps, test projects and older target frameworks. An explicit `<IsAotCompatible>false</IsAotCompatible>` also silences it, because that is a decision. The diagnostic shows on build and in full-solution analysis.

### Fix

```xml
<!-- In a multi-targeted library, set it only where it applies -->
<PropertyGroup Condition="$([MSBuild]::IsTargetFrameworkCompatible('$(TargetFramework)', 'net8.0'))">
  <IsAotCompatible>true</IsAotCompatible>
</PropertyGroup>
```

### Suppression

The diagnostic has no source location, so `#pragma` cannot silence it. Record the decision instead:

```xml
<IsAotCompatible>false</IsAotCompatible>
```

or turn the rule off in `.editorconfig`: `dotnet_diagnostic.ZA1706.severity = none`.

---

## ZA1707 — Use the configuration-binding source generator {#za1707}

> **Severity**: Info | **Min TFM**: Any (Binder 8.0+, C# 12) | **Code fix**: No

### Why

`ConfigurationBinder.Bind`, `Get` and `GetValue`, and the options helpers `Configure<T>(IConfiguration)`, `Bind` and `BindConfiguration`, walk your options type with reflection. That needs the type's members preserved from trimming, and it is slower than generated code. Since .NET 8, the configuration-binding source generator intercepts those calls and replaces them with generated code. Turning it on is one MSBuild property; the calls themselves stay the same.

`PublishAot` turns the generator on automatically, so the rule stays silent there, and it also stays silent once the SDK's own AOT analyzer is enabled.

Calls that bind a type known only at run time, such as `Get(Type)` with a non-`typeof` argument or `Bind(object)`, are not reported because the generator cannot replace them. Neither are calls the generator refuses with warning SYSLIB1104: a bound type that is or contains a generic type parameter, such as `Get<T>()` or `Get<List<T>>()` inside a generic method, and a bound type that is, sits inside or contains a private or protected type, which the generated code cannot name.

The generator intercepts the calls, which needs C# 12; below it the generator fails the build with error SYSLIB1102. The rule therefore reports only from C# 12, and only with Microsoft.Extensions.Configuration.Binder 8.0 or later.

### Fix

```xml
<PropertyGroup>
  <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>
</PropertyGroup>
```

### Suppression

```csharp
#pragma warning disable ZA1707
// or in .editorconfig: dotnet_diagnostic.ZA1707.severity = none
```

---

## ZA1708 — Use a source-generated options validator {#za1708}

> **Severity**: Info | **Min TFM**: Any (Options 8.0+, C# 8) | **Code fix**: No

### Why

`ValidateDataAnnotations()` reads your options type's `[Required]`, `[Range]` and other attributes with reflection every time the options are validated. That is not trim-safe, and it costs reflection at startup. Since .NET 8, the `[OptionsValidator]` source generator writes the same checks as plain code: declare a `partial` validator class for your options type and register it instead. The rule only reports where `[OptionsValidator]` is available, that is with Microsoft.Extensions.Options 8.0 or later, and from C# 8, because below it the generator fails the build with error SYSLIB1216.

The options type keeps its DataAnnotations attributes, because the generator reads them to write the checks. `.ValidateOnStart()` still works with a registered validator, so keep it if you validate at startup.

### Before

```csharp
// ❌ reflection over the attributes at validation time
services.AddOptions<SmtpOptions>()
    .BindConfiguration("Smtp")
    .ValidateDataAnnotations();
```

### After

```csharp
using Microsoft.Extensions.Options;

// ✓ the generator writes the validation code
[OptionsValidator]
public partial class SmtpOptionsValidator : IValidateOptions<SmtpOptions>;

services.AddOptions<SmtpOptions>().BindConfiguration("Smtp");
services.AddSingleton<IValidateOptions<SmtpOptions>, SmtpOptionsValidator>();
```

### Suppression

```csharp
#pragma warning disable ZA1708
// or in .editorconfig: dotnet_diagnostic.ZA1708.severity = none
```

---

## ZA1709 — Avoid dynamic dispatch {#za1709}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

Every operation on a `dynamic` value is bound at run time by the C# runtime binder. That covers member access, method calls, indexers, operators, and conversions back to a static type. The binder inspects the object with reflection and generates code on the fly, which Native AOT cannot do and which trimming breaks. Dynamic dispatch is also far slower than a static call and allocates on every operation.

The rule reports each expression that dispatches dynamically once, at its outermost dynamic operation. That includes `await` on a dynamic value, `foreach` over one, and a dynamic value used as a condition.

Declaring or storing a `dynamic` value is not reported, because that does not call the binder. Returning one is not reported when the method's return type is `dynamic` or `object`. Returning it through any other static return type converts it to that type, and that conversion goes through the binder, so it is reported. An `is` or `as` type test on a dynamic value is not reported either: both check the run-time type without the binder.

### Before

```csharp
// storing the result in a dynamic local does not call the binder
dynamic response = JsonConvert.DeserializeObject(json);
// ❌ the runtime binder resolves user, then name, then the conversion to string
string name = response.user.name;
```

### After

```csharp
// ✓ a typed model binds at compile time
var response = JsonSerializer.Deserialize(json, AppJsonContext.Default.Response);
string name = response.User.Name;
```

### Suppression

```csharp
#pragma warning disable ZA1709
// or in .editorconfig: dotnet_diagnostic.ZA1709.severity = none
```

---

## ZA1710 — Avoid assembly-scanning registration {#za1710}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

Scrutor's `Scan`, MediatR's `RegisterServicesFromAssembly*`, FluentValidation's `AddValidatorsFromAssembly*` and AutoMapper's `AddAutoMapper` with an assembly or type argument all find the types to register by walking an assembly with reflection at start-up. Trimming removes the types nobody references by name, so the scan finds fewer than it did in debug, and Native AOT cannot enumerate and instantiate them at all. The scan also costs start-up time on every launch.

The rule recognises Scrutor, MediatR, FluentValidation and AutoMapper, and matches on the assembly that declares the called method and on the method's name, so it keeps working across their major versions. The message ends with advice that differs per library, because the ZeroAlloc replacements work differently. For Scrutor, ZeroAlloc.Inject registers services at compile time with a source generator. For MediatR, ZeroAlloc.Mediator dispatches without reflection, but its generated `AddMediator()` registers only `IMediator`, so the handlers are registered with ZeroAlloc.Inject. For FluentValidation, ZeroAlloc.Validation registers validators at compile time. For AutoMapper, ZeroAlloc.Mapping generates static mappers, so no registration is needed. Overloads that take only a configuration delegate, such as `AddAutoMapper(cfg => { })`, scan nothing and are not reported. MediatR 12's `AddMediatR(cfg => ...)` is not reported itself, but each `RegisterServicesFrom*` call inside it is.

Unlike the other ZA17xx rules, this one stays on when the SDK's AOT analyzer is enabled, because these libraries are not trim-annotated and the SDK says nothing about their scanning calls. It stands down for a call only when the called method, or its containing type, carries `RequiresUnreferencedCode` and the SDK's trim analyzer is on (`PublishAot`, `IsAotCompatible`, `EnableTrimAnalyzer`, `PublishTrimmed` or `IsTrimmable`), or carries `RequiresDynamicCode` and the SDK's AOT analyzer is on (`PublishAot`, `IsAotCompatible` or `EnableAotAnalyzer`), since the SDK reports that call already.

### Before

```csharp
// Program.cs: every registration discovers its types by scanning an assembly
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));              // ❌ ZA1710
builder.Services.AddValidatorsFromAssemblyContaining<OrderValidator>();       // ❌ ZA1710
builder.Services.AddAutoMapper(typeof(Program).Assembly);                     // ❌ ZA1710
```

### After

```csharp
// ✓ handlers and services are marked, and the generated methods register them
[Transient]
public class CreateOrderHandler : IRequestHandler<CreateOrder, OrderId> { /* ... */ }

builder.Services.AddMediator();            // ZeroAlloc.Mediator: registers IMediator only
builder.Services.AddMyAppServices();       // ZeroAlloc.Inject: registers the marked handlers and services
builder.Services.AddZeroAllocValidators(); // ZeroAlloc.Validation.Inject: validators found by the source generator
```

`AddMyAppServices()` is the method ZeroAlloc.Inject generates from the assembly name, so `MyApp` gives `AddMyAppServices()`. For Scrutor, mark each service with `[Transient]`, `[Scoped]` or `[Singleton]` the same way. ZeroAlloc.Mapping replaces AutoMapper's `IMapper` with static mapper calls: declare `[Map<OrderRequest, Order>]` on a `static partial class` and call the generated `Map` method directly, so nothing is registered.

### Suppression

```csharp
#pragma warning disable ZA1710
// or in .editorconfig: dotnet_diagnostic.ZA1710.severity = none
```
