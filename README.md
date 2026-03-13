# ZeroAlloc Analyzers

Roslyn analyzers for modern .NET performance patterns with multi-TFM awareness. Detects allocation-heavy patterns that existing analyzers miss and suggests zero/low-allocation alternatives.

## Installation

```xml
<PackageReference Include="ZeroAlloc.Analyzers" Version="1.0.0" PrivateAssets="all" />
```

## Rules

| ID | Description | Severity | Min TFM |
|----|-------------|----------|---------|
| **ZA01xx — Collections** | | | |
| [ZA0101](docs/rules/ZA0101.md) | Use FrozenDictionary for read-only dictionary | Info | net8.0 |
| [ZA0102](docs/rules/ZA0102.md) | Use FrozenSet for read-only set | Info | net8.0 |
| [ZA0103](docs/rules/ZA0103.md) | Use CollectionsMarshal.AsSpan for List iteration | Info | net5.0 |
| [ZA0104](docs/rules/ZA0104.md) | Use SearchValues/FrozenSet for repeated lookups | Info | net8.0 |
| [ZA0105](docs/rules/ZA0105.md) | Use TryGetValue instead of ContainsKey + indexer | Warning | Any |
| [ZA0106](docs/rules/ZA0106.md) | Avoid premature ToList/ToArray before LINQ | Warning | Any |
| [ZA0107](docs/rules/ZA0107.md) | Pre-size collections when capacity is known | Info | Any |
| [ZA0108](docs/rules/ZA0108.md) | Avoid redundant ToList/ToArray materialization | Warning | Any |
| [ZA0109](docs/rules/ZA0109.md) | Avoid zero-length array allocation, use Array.Empty<T>() | Warning | Any |
| **ZA02xx — Strings** | | | |
| [ZA0201](docs/rules/ZA0201.md) | Avoid string concatenation in loops | Warning | Any |
| [ZA0202](docs/rules/ZA0202.md) | Avoid chained string.Replace calls | Info | Any |
| [ZA0203](docs/rules/ZA0203.md) | Use AsSpan instead of Substring | Info | net5.0 |
| [ZA0204](docs/rules/ZA0204.md) | Use string.Create instead of string.Format | Info | net6.0 |
| [ZA0205](docs/rules/ZA0205.md) | Use CompositeFormat for repeated format strings | Info | net8.0 |
| [ZA0206](docs/rules/ZA0206.md) | Avoid span.ToString() before Parse | Info | net6.0 |
| [ZA0208](docs/rules/ZA0208.md) | Avoid string.Join overload that boxes non-string elements | Warning | Any |
| [ZA0209](docs/rules/ZA0209.md) | Avoid value type boxing in string concatenation | Warning | Any |
| **ZA03xx — Memory** | | | |
| [ZA0301](docs/rules/ZA0301.md) | Use stackalloc for small fixed-size arrays | Info | Any |
| [ZA0302](docs/rules/ZA0302.md) | Use ArrayPool for large temporary arrays | Info | Any |
| **ZA04xx — Logging** | | | |
| [ZA0401](docs/rules/ZA0401.md) | Use LoggerMessage source generator | Info | net6.0 |
| **ZA05xx — Boxing** | | | |
| [ZA0501](docs/rules/ZA0501.md) | Avoid boxing value types in loops | Warning | Any |
| [ZA0502](docs/rules/ZA0502.md) | Avoid closure allocations in loops | Info | Any |
| [ZA0504](docs/rules/ZA0504.md) | Avoid defensive copies on in/readonly structs | Info | Any |
| **ZA06xx — LINQ & Params** | | | |
| [ZA0601](docs/rules/ZA0601.md) | Avoid LINQ methods in loops | Warning | Any |
| [ZA0602](docs/rules/ZA0602.md) | Avoid params calls in loops | Info | Any |
| [ZA0603](docs/rules/ZA0603.md) | Use .Count/.Length instead of LINQ .Count() | Info | Any |
| [ZA0604](docs/rules/ZA0604.md) | Use .Count > 0 instead of LINQ .Any() | Info | Any |
| [ZA0605](docs/rules/ZA0605.md) | Use indexer instead of LINQ .First()/.Last() | Info | Any |
| [ZA0606](docs/rules/ZA0606.md) | Avoid foreach over interface-typed collection variable | Warning | Any |
| [ZA0607](docs/rules/ZA0607.md) | Avoid multiple enumeration of IEnumerable<T> | Warning | Any |
| **ZA07xx — Regex** | | | |
| [ZA0701](docs/rules/ZA0701.md) | Use GeneratedRegex for compile-time regex | Info | net7.0 |
| **ZA08xx — Enums** | | | |
| [ZA0801](docs/rules/ZA0801.md) | Avoid Enum.HasFlag (boxing on < net7.0) | Info | < net7.0 |
| [ZA0802](docs/rules/ZA0802.md) | Avoid Enum.ToString() allocations | Info | Any |
| [ZA0803](docs/rules/ZA0803.md) | Cache Enum.GetName/GetValues in loops | Info | Any |
| **ZA09xx — Sealing** | | | |
| [ZA0901](docs/rules/ZA0901.md) | Consider sealing classes for devirtualization | Info | Any |
| **ZA10xx — Serialization** | | | |
| [ZA1001](docs/rules/ZA1001.md) | Use JSON source generation instead of reflection | Info | net7.0 |
| **ZA11xx — Async** | | | |
| [ZA1101](docs/rules/ZA1101.md) | Elide async/await on simple tail calls | Info | Any |
| [ZA1102](docs/rules/ZA1102.md) | Dispose CancellationTokenSource | Info | Any |
| [ZA1104](docs/rules/ZA1104.md) | Avoid Span<T> in async methods, use Memory<T> instead | Warning | Any |
| **ZA14xx — Delegates** | | | |
| [ZA1401](docs/rules/ZA1401.md) | Use static lambda when no capture needed | Info | net5.0 |
| **ZA15xx — Value Types** | | | |
| [ZA1501](docs/rules/ZA1501.md) | Override GetHashCode on struct dictionary keys | Info | Any |
| [ZA1502](docs/rules/ZA1502.md) | Avoid finalizers, use IDisposable | Info | Any |

## TFM Compatibility

Rules are automatically enabled/disabled based on your project's `TargetFramework`. The package includes a `.props` file that flows `TargetFramework` to the analyzers via `CompilerVisibleProperty`.

| TFM | Active Rules |
|-----|-------------|
| net8.0+ | All 42 rules |
| net7.0 | All except ZA0101, ZA0102, ZA0104, ZA0205, ZA0801 |
| net6.0 | All except ZA0101, ZA0102, ZA0104, ZA0205, ZA0701, ZA0801, ZA1001 |
| net5.0 | ZA0103, ZA0105-ZA0109, ZA0201-ZA0203, ZA0208, ZA0209, ZA0301, ZA0302, ZA0501, ZA0502, ZA0504, ZA0601-ZA0607, ZA0801-ZA0803, ZA0901, ZA1101, ZA1102, ZA1104, ZA1401, ZA1501, ZA1502 |
| < net5.0 | ZA0105-ZA0109, ZA0201, ZA0202, ZA0208, ZA0209, ZA0301, ZA0302, ZA0501, ZA0502, ZA0504, ZA0601-ZA0607, ZA0802, ZA0803, ZA0901, ZA1101, ZA1102, ZA1104, ZA1501, ZA1502 |

## License

MIT
