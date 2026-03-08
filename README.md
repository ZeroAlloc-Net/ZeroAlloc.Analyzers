# ZeroAlloc Analyzers

Roslyn analyzers for modern .NET performance patterns with multi-TFM awareness. Detects allocation-heavy patterns that existing analyzers miss and suggests zero/low-allocation alternatives.

> **Work in progress** — not yet published to NuGet.

## Installation

```xml
<PackageReference Include="ZeroAlloc.Analyzers.Package" Version="1.0.0" PrivateAssets="all" />
```

## Rules

| ID | Description | Severity | Min TFM |
|----|-------------|----------|---------|
| [ZA0001](docs/rules/ZA0001.md) | Use FrozenDictionary for read-only dictionary | Info | net8.0 |
| [ZA0002](docs/rules/ZA0002.md) | Use FrozenSet for read-only set | Info | net8.0 |
| [ZA0003](docs/rules/ZA0003.md) | Use CollectionsMarshal.AsSpan for List iteration | Info | net5.0 |
| [ZA0004](docs/rules/ZA0004.md) | Use SearchValues/FrozenSet for repeated lookups | Info | net8.0 |
| [ZA0005](docs/rules/ZA0005.md) | Avoid string concatenation in loops | Warning | Any |
| [ZA0006](docs/rules/ZA0006.md) | Use stackalloc for small fixed-size arrays | Info | Any |
| [ZA0007](docs/rules/ZA0007.md) | Use ArrayPool for large temporary arrays | Info | Any |
| [ZA0008](docs/rules/ZA0008.md) | Avoid Enum.HasFlag (boxing on < net7.0) | Info | < net7.0 |
| [ZA0009](docs/rules/ZA0009.md) | Avoid chained string.Replace calls | Info | Any |
| [ZA0010](docs/rules/ZA0010.md) | Use TryGetValue instead of ContainsKey + indexer | Warning | Any |

## TFM Compatibility

Rules are automatically enabled/disabled based on your project's `TargetFramework`. The package includes a `.props` file that flows `TargetFramework` to the analyzers via `CompilerVisibleProperty`.

| TFM | Active Rules |
|-----|-------------|
| net8.0+ | All 10 rules |
| net7.0 | All except ZA0001, ZA0002, ZA0004, ZA0008 |
| net5.0-net6.0 | ZA0003, ZA0005-ZA0007, ZA0008-ZA0010 |
| < net5.0 | ZA0005-ZA0007, ZA0009, ZA0010 |

## License

MIT
