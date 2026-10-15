# ZeroAlloc.Analyzers

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Analyzers.svg)](https://www.nuget.org/packages/ZeroAlloc.Analyzers)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Roslyn analyzers for modern .NET performance patterns. ZeroAlloc.Analyzers catches allocation-heavy patterns that built-in analyzers miss — FrozenDictionary opportunities, LINQ iterator overhead, boxing in loops, async state machine waste, and more — with 64 rules across 15 categories. Every rule is multi-TFM aware: rules that require a specific .NET version are automatically silenced when your project targets an older framework, so every diagnostic you see is actionable.

## Installation

```bash
dotnet add package ZeroAlloc.Analyzers
```

## Example

```csharp
// warning ZA0105: Use 'TryGetValue' instead of 'ContainsKey' followed by indexer access
// Before — two dictionary lookups
if (_cache.ContainsKey(key))
    return _cache[key];

// After — single lookup
if (_cache.TryGetValue(key, out var value))
    return value;
```

```csharp
// warning ZA0201: String concatenation in a loop causes repeated allocations — use StringBuilder
// Before
string result = "";
foreach (var item in items)
    result += item + ", ";   // allocates a new string every iteration

// After
var sb = new System.Text.StringBuilder();
foreach (var item in items)
    sb.Append(item).Append(", ");
string result = sb.ToString();
```

## Performance

On a 240-file sample project the package added a median of 1–4% to a `net8.0` build, 4–5% to a `net8.0` + `netstandard2.0` build and 10–23% to a `netstandard2.0` build, relative to the same project built without it; see [Measured overhead](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/performance.md#measured-overhead) for the method, the spread and the harness to reproduce it. Changing any file reruns the analyzers over the whole project; a build with no changes does not run them. TFM-gated rules that do not apply to your target framework add no per-file work.

| Scenario | Rules active |
|---|---|
| `netstandard2.0` single-TFM | 41 of 64, or 44 with the System.Memory and assembly-scanning library packages |
| `net8.0` single-TFM | 59 of 64, or 62 with the Configuration.Binder, Options and assembly-scanning library packages |
| `net8.0` + `netstandard2.0` multi-TFM | 59 / 41 per TFM, or 62 / 44 with the Configuration.Binder, Options, assembly-scanning library and System.Memory packages |
| `net8.0`, data-flow rules disabled (ZA0607, ZA0502) | 57 of 64 |

A rule counts as active when it can report with no extra packages at the target's default language version. ZA1707 and ZA1708 need the Microsoft.Extensions.Configuration.Binder and Microsoft.Extensions.Options packages. They do not run on `netstandard2.0` even with those packages, because its default C# 7.3 is below their C# 12 and C# 8 gates.

See [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/performance.md) for tuning tips.

## Features

- **64 rules** across 15 categories: Collections, Strings, Memory, Logging, Boxing, LINQ, Regex, Enums, Sealing, Serialization, Async, Delegates, Value Types, Data Layout, Native AOT
- **Multi-TFM aware** — rules requiring net5.0+, net6.0+, net7.0+, or net8.0+ are automatically gated; you never see a diagnostic for an API that does not exist in your target
- **Code fixes** included for a subset of rules — apply suggestions with one click from the IDE or via `dotnet format`
- **Zero transitive dependency** — the package is a development dependency, so `dotnet add package` marks it `PrivateAssets="all"` and it does not propagate to your consumers
- **IDE + CLI** — diagnostics surface in Visual Studio, Rider, VS Code with C# Dev Kit, and `dotnet build` output

## Documentation

| Page | Description |
|------|-------------|
| [Getting Started](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/getting-started.md) | Install the package, understand first diagnostics, IDE setup, TFM awareness |
| [Configuration](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/configuration.md) | Severity tuning, suppression, TFM gating, TreatWarningsAsErrors |
| [Collections (ZA01xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/collections.md) | FrozenDictionary, FrozenSet, TryGetValue, pre-sizing, zero-length arrays |
| [Strings (ZA02xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/strings.md) | StringBuilder, AsSpan, string.Create, CompositeFormat, boxing in concatenation |
| [Memory (ZA03xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/memory.md) | stackalloc for small buffers, ArrayPool for large temporary arrays, returning rented arrays, constant tables as spans |
| [Logging (ZA04xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/logging.md) | LoggerMessage source generator vs reflection-based logging |
| [Boxing (ZA05xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/boxing.md) | Value type boxing in loops, closure allocations, defensive copies |
| [LINQ (ZA06xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/linq.md) | LINQ in loops, Count vs Any, indexer vs First/Last, multiple enumeration |
| [Regex (ZA07xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/regex.md) | GeneratedRegex source generator vs runtime regex compilation |
| [Enums (ZA08xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/enums.md) | HasFlag boxing, Enum.ToString allocations, GetName/GetValues in loops |
| [Sealing (ZA09xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/sealing.md) | Class sealing for JIT devirtualization |
| [Serialization (ZA10xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/serialization.md) | JSON source generation vs reflection-based serialization |
| [Async (ZA11xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/async.md) | Elide async/await on tail calls, dispose CancellationTokenSource, Span in async |
| [Delegates (ZA14xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/delegates.md) | Static lambda caching, closure elimination, state-passing overloads |
| [Value Types (ZA15xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/value-types.md) | Struct GetHashCode override and IEquatable on hash keys, avoid finalizers |
| [Data Layout (ZA16xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/data-layout.md) | Cache-line aware struct field packing, false-sharing isolation |
| [Native AOT (ZA17xx)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/aot.md) | Expression.Compile, Reflection.Emit, runtime generics, reflection serializers, name-based loading, AOT-compatible libraries, configuration binding, options validation, dynamic, assembly-scanning registration |
| [Build Performance](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/performance.md) | Analyzer build-time overhead, TFM gating, CI vs local configuration |
| [Testing with Analyzers](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/testing.md) | Suppress warnings in tests, write Roslyn diagnostic tests, TFM-gated rule testing |

## License

MIT
