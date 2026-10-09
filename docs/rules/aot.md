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

When a project sets `<PublishAot>true</PublishAot>` or `<IsAotCompatible>true</IsAotCompatible>`, the .NET SDK enables the official AOT analyzer (`IL3050`+) and trimming analyzer (`IL2xxx`), which authoritatively cover these patterns. To avoid double-reporting, **every ZA17xx rule automatically stands down when that opt-in is detected** (via the `PublishAot`, `IsAotCompatible`, or `EnableAotAnalyzer` MSBuild properties).

The value ZA17xx adds is for the **common case where you have not opted in yet** — for example a library that has not set `IsAotCompatible`. There the SDK analyzers are silent, and these rules surface AOT hazards early so the eventual move to AOT is smaller.

Several existing ZeroAlloc rules already steer toward AOT-friendly code: [ZA1001](serialization.md#za1001) (JSON source generation), [ZA0701](regex.md#za0701) (GeneratedRegex), and [ZA0401](logging.md#za0401) (LoggerMessage).

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

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`ConfigurationBinder.Bind`, `Get` and `GetValue`, and the options helpers `Configure<T>(IConfiguration)`, `Bind` and `BindConfiguration`, walk your options type with reflection. That needs the type's members preserved from trimming, and it is slower than generated code. Since .NET 8, the configuration-binding source generator intercepts those calls and replaces them with generated code. Turning it on is one MSBuild property; the calls themselves stay the same.

`PublishAot` turns the generator on automatically, so the rule stays silent there, and it also stays silent once the SDK's own AOT analyzer is enabled.

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
