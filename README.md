# ZeroAlloc.Analyzers

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Analyzers.svg)](https://www.nuget.org/packages/ZeroAlloc.Analyzers)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Roslyn analyzers for modern .NET performance patterns. ZeroAlloc.Analyzers catches allocation-heavy patterns that built-in analyzers miss — FrozenDictionary opportunities, LINQ iterator overhead, boxing in loops, async state machine waste, and more — with 43 rules across 13 categories. Every rule is multi-TFM aware: rules that require a specific .NET version are automatically silenced when your project targets an older framework, so every diagnostic you see is actionable.

## Installation

```bash
dotnet add package ZeroAlloc.Analyzers
```

## Example

```csharp
// warning ZA0105: Use TryGetValue instead of ContainsKey + indexer access.
// Before — two dictionary lookups
if (_cache.ContainsKey(key))
    return _cache[key];

// After — single lookup, zero extra allocation
if (_cache.TryGetValue(key, out var value))
    return value;
```

```csharp
// warning ZA0201: Avoid string concatenation in loops; use StringBuilder or an interpolated string handler.
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

Roslyn analyzers run incrementally; on a warmed-up build only changed files are re-analyzed, so the steady-state cost is proportional to the number of files you actually edit, not your whole codebase. TFM-gated rules that do not apply to your target framework register zero callbacks and add zero per-file overhead.

| Scenario | Rules active | Typical first-build overhead | Incremental overhead |
|---|---|---|---|
| `netstandard2.0` single-TFM | 29 of 43 | ~120 ms | ~10 ms |
| `net8.0` single-TFM | 43 of 43 | ~200 ms | ~15 ms |
| `net8.0` + `netstandard2.0` multi-TFM | 43 / 29 per TFM | ~350 ms | ~25 ms |
| `net8.0`, data-flow rules disabled (ZA0607, ZA0502) | 41 of 43 | ~160 ms | ~10 ms |

See [docs/performance.md](docs/performance.md) for tuning tips.

## Features

- **43 rules** across 13 categories: Collections, Strings, Memory, Logging, Boxing, LINQ, Regex, Enums, Sealing, Serialization, Async, Delegates, Value Types
- **Multi-TFM aware** — rules requiring net5.0+, net6.0+, net7.0+, or net8.0+ are automatically gated; you never see a diagnostic for an API that does not exist in your target
- **Code fixes** included for a subset of rules — apply suggestions with one click from the IDE or via `dotnet format`
- **Zero transitive dependency** — install with `PrivateAssets="all"` so the package does not propagate to your consumers
- **IDE + CLI** — diagnostics surface in Visual Studio, Rider, VS Code with C# Dev Kit, and `dotnet build` output

## Documentation

| Page | Description |
|------|-------------|
| [Getting Started](docs/getting-started.md) | Install the package, understand first diagnostics, IDE setup, TFM awareness |
| [Configuration](docs/configuration.md) | Severity tuning, suppression, TFM gating, TreatWarningsAsErrors |
| [Collections (ZA01xx)](docs/rules/collections.md) | FrozenDictionary, FrozenSet, TryGetValue, pre-sizing, zero-length arrays |
| [Strings (ZA02xx)](docs/rules/strings.md) | StringBuilder, AsSpan, string.Create, CompositeFormat, boxing in concatenation |
| [Memory (ZA03xx)](docs/rules/memory.md) | stackalloc for small buffers, ArrayPool for large temporary arrays |
| [Logging (ZA04xx)](docs/rules/logging.md) | LoggerMessage source generator vs reflection-based logging |
| [Boxing (ZA05xx)](docs/rules/boxing.md) | Value type boxing in loops, closure allocations, defensive copies |
| [LINQ (ZA06xx)](docs/rules/linq.md) | LINQ in loops, Count vs Any, indexer vs First/Last, multiple enumeration |
| [Regex (ZA07xx)](docs/rules/regex.md) | GeneratedRegex source generator vs runtime regex compilation |
| [Enums (ZA08xx)](docs/rules/enums.md) | HasFlag boxing, Enum.ToString allocations, GetName/GetValues in loops |
| [Sealing (ZA09xx)](docs/rules/sealing.md) | Class sealing for JIT devirtualization |
| [Serialization (ZA10xx)](docs/rules/serialization.md) | JSON source generation vs reflection-based serialization |
| [Async (ZA11xx)](docs/rules/async.md) | Elide async/await on tail calls, dispose CancellationTokenSource, Span in async |
| [Delegates (ZA14xx)](docs/rules/delegates.md) | Static lambda caching, closure elimination |
| [Value Types (ZA15xx)](docs/rules/value-types.md) | Struct GetHashCode override, avoid finalizers |
| [Build Performance](docs/performance.md) | Analyzer build-time overhead, TFM gating, CI vs local configuration |
| [Testing with Analyzers](docs/testing.md) | Suppress warnings in tests, write Roslyn diagnostic tests, TFM-gated rule testing |

## License

MIT
