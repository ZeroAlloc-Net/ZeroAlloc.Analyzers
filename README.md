# ZeroAlloc.Analyzers

[![NuGet](https://img.shields.io/nuget/v/ZeroAlloc.Analyzers.svg)](https://www.nuget.org/packages/ZeroAlloc.Analyzers)
[![Build](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/actions/workflows/ci.yml)
[![codecov](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Analyzers/graph/badge.svg)](https://codecov.io/gh/ZeroAlloc-Net/ZeroAlloc.Analyzers)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/LICENSE)
[![AOT](https://img.shields.io/badge/AOT--Compatible-passing-brightgreen)](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
[![GitHub Sponsors](https://img.shields.io/github/sponsors/MarcelRoozekrans?style=flat&logo=githubsponsors&color=ea4aaa&label=Sponsor)](https://github.com/sponsors/MarcelRoozekrans)

Roslyn analyzers that catch allocations and NativeAOT breakers at compile time: 64 rules for the performance patterns the built-in .NET analyzers miss, each silenced automatically where your target framework can't use the fix.

## Why ZeroAlloc.Analyzers

- **Find allocations in the build, not in a profiler.** Boxing in string concatenation, LINQ in loops, double dictionary lookups, closures, uncached delegates and missed `FrozenDictionary` opportunities show up as warnings where you write the code.
- **Catch NativeAOT breakers early.** The ZA17xx rules flag `Expression.Compile`, `Reflection.Emit`, reflection-based serializers, `dynamic` and assembly-scanning registration before a trimmed or AOT publish fails.
- **Every diagnostic is actionable.** Rules that need .NET 5, 6, 7, 8 or 9, or a newer C# version, are silenced on older targets, so you never see a fix for an API you can't call.
- **A few percent of build time.** On a 240-file sample, a median of +2.1% for a full `net8.0` build and +2.9% for a one-file edit; a build with no changes runs no analyzers at all.

## Install

```bash
dotnet add package ZeroAlloc.Analyzers
```

It is a development dependency: nothing flows to your consumers, and there is no runtime assembly.

## Quick start

Build, and the diagnostics appear in `dotnet build`, Visual Studio, Rider and VS Code with C# Dev Kit:

```csharp
public sealed class PriceCache
{
    private readonly Dictionary<string, decimal> _prices = new();

    public decimal Get(string sku)
    {
        // warning ZA0105: Use 'TryGetValue' instead of 'ContainsKey' followed by indexer access
        if (_prices.ContainsKey(sku))
            return _prices[sku];
        return 0m;
    }
}
```

Apply the code fix from the lightbulb, or with `dotnet format`, and the two lookups become one:

```csharp
        if (_prices.TryGetValue(sku, out var value))
            return value;
```

## What you get

- **64 rules in 15 categories**, each with its own page: [collections](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/collections.md), [strings](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/strings.md), [memory](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/memory.md), [logging](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/logging.md), [boxing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/boxing.md), [LINQ](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/linq.md), [regex](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/regex.md), [enums](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/enums.md), [sealing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/sealing.md), [serialization](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/serialization.md), [async](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/async.md), [delegates](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/delegates.md), [value types](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/value-types.md), [data layout](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/data-layout.md) and [Native AOT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/rules/aot.md).
- **Code fixes** for eight rules, applied from the IDE or with `dotnet format`.
- **Multi-target aware**: each target framework of a multi-targeted project gets exactly the rules that apply to it.
- **Standard severity control** through `.editorconfig`, including `TreatWarningsAsErrors`. [Configuration](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/configuration.md)
- **Guidance for test projects**, where some rules are noise. [Testing](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/testing.md)

## Performance

Build overhead on a 240-file, 18,000-line sample, relative to the same project built without the package; median of 15 interleaved pairs.

| Target | Full build | One-file edit |
|---|---:|---:|
| `net8.0` | +2.1% | +2.9% |
| `net8.0` + `netstandard2.0` | +4.3% | +5.4% |
| `netstandard2.0` | +10.5% | +23.0% |

A build with no changes does not run the compiler, so the analyzers add no work. The overhead is largest on `netstandard2.0`, where the SDK's own analyzers don't run and the build is smaller.

How many rules run per target, the spread, the environment, the harness to reproduce it and tips for fast local builds: [docs/performance.md](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/docs/performance.md).

## Learn more

- Documentation: [analyzers.zeroalloc.net](https://analyzers.zeroalloc.net)
- Related packages: [ZeroAlloc.TestHelpers](https://github.com/ZeroAlloc-Net/ZeroAlloc.TestHelpers) proves zero-allocation claims in tests, and the [ZeroAlloc family](https://github.com/ZeroAlloc-Net) of libraries, which build with these rules
- Contributing: issues and pull requests are welcome on [GitHub](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/issues); see the [contributor docs](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/tree/main/docs/contributing)
- License: [MIT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Analyzers/blob/main/LICENSE)
