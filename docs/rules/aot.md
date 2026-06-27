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
