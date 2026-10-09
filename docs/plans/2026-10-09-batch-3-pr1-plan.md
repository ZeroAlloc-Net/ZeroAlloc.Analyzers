# Batch 3 PR 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the seven batch 3 rules in the Collections, Strings and Memory categories: ZA0110, ZA0111, ZA0210, ZA0211, ZA0212, ZA0303 and ZA0304.

**Architecture:** Each rule is one `DiagnosticAnalyzer` in `src/ZeroAlloc.Analyzers/Analyzers/`, built on `IOperation` and gated on symbols that exist in the compilation. Code fixes live in `src/ZeroAlloc.Analyzers.CodeFixes/`. ZA0110 and ZA0304 have to see every use of a symbol across the compilation, so they report from a compilation-end action.

**Tech Stack:** C# with `LangVersion` latest, Roslyn 5.9 (`Microsoft.CodeAnalysis.CSharp`), and xUnit with `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` 1.1.4.

**Spec:** `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`. Issues #73 to #79.

## Global Constraints

- The analyzer project targets `netstandard2.0`. `TreatWarningsAsErrors`, `Nullable` and `ImplicitUsings` are on, and so is `EnforceExtendedAnalyzerRules`. Do not add explicit `using System.Linq;`, `using System.Collections.Generic;` or `using System.Threading.Tasks;`; implicit usings already provide them.
- Every new descriptor needs a `DiagnosticIds` constant and a row in `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md`. The RS2000 release-tracking analyzer fails the build otherwise.
- A descriptor reported from a compilation-end action must pass `customTags: WellKnownDiagnosticTags.CompilationEnd`. RS1037 fails the build otherwise.
- Analyze through `IOperation`. Gate on BCL APIs by checking that their symbol exists, not by parsing the TFM. Gate on language features with `((CSharpCompilation)compilation).LanguageVersion`.
- Severities: ZA0210 and ZA0303 are `Warning`, all others `Info`.
- Make one commit per rule, with a `feat:` header. Commit bodies must not contain nested parentheses, because release-please drops the whole commit.
- Commit trailer: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`
- Build and test from the repo root: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release`

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/ZeroAlloc.Analyzers/DiagnosticIds.cs` | One constant per rule | every task |
| `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md` | Release tracking row per rule | every task |
| `src/ZeroAlloc.Analyzers/Analyzers/ReturnRentedArrayAnalyzer.cs` | ZA0303 | 1 |
| `src/ZeroAlloc.Analyzers/Analyzers/UseUtf8StringLiteralAnalyzer.cs` | ZA0210 | 2 |
| `src/ZeroAlloc.Analyzers.CodeFixes/UseUtf8StringLiteralCodeFixProvider.cs` | ZA0210 fix | 2 |
| `tests/ZeroAlloc.Analyzers.Tests/Verifiers/CSharpAnalyzerVerifier.cs` | Add a `LanguageVersion` overload | 2 |
| `src/ZeroAlloc.Analyzers/ReadOnlySpanUse.cs` | Shared check: is this array use still valid after the array becomes a `ReadOnlySpan<T>`? | 3 |
| `src/ZeroAlloc.Analyzers/Analyzers/UseReadOnlySpanForConstantTableAnalyzer.cs` | ZA0304 | 3 |
| `src/ZeroAlloc.Analyzers.CodeFixes/UsingDirectives.cs` | Shared "add `using System;`" helper, extracted from the ZA0109 fix | 3 |
| `src/ZeroAlloc.Analyzers.CodeFixes/AvoidZeroLengthArrayAllocationCodeFixProvider.cs` | Switch to the shared helper | 3 |
| `src/ZeroAlloc.Analyzers.CodeFixes/UseReadOnlySpanForConstantTableCodeFixProvider.cs` | ZA0304 fix | 3 |
| `src/ZeroAlloc.Analyzers/Analyzers/AvoidConcurrentDictionarySnapshotAnalyzer.cs` | ZA0111 | 4 |
| `src/ZeroAlloc.Analyzers.CodeFixes/AvoidConcurrentDictionarySnapshotCodeFixProvider.cs` | ZA0111 fix | 4 |
| `src/ZeroAlloc.Analyzers/Analyzers/UseTryParseAnalyzer.cs` | ZA0212 | 5 |
| `src/ZeroAlloc.Analyzers/Analyzers/UseSpanSplitAnalyzer.cs` | ZA0211 | 6 |
| `src/ZeroAlloc.Analyzers/Analyzers/PreferParamsSpanAnalyzer.cs` | ZA0110 | 7 |
| `src/ZeroAlloc.Analyzers.CodeFixes/PreferParamsSpanCodeFixProvider.cs` | ZA0110 fix | 7 |
| `docs/rules/{collections,strings,memory}.md` | One section per rule | each task |
| `docs/getting-started.md`, `README.md` | Rule table rows, rule counts | each task, and 8 |
| `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md` | Record the refinements this plan makes | 8 |

## Refinements to the spec made by this plan

Task 8 writes these back into the design doc:

1. **ZA0303 trusted callees.** Passing the rented array to a method of `System.MemoryExtensions`, `System.Array`, `System.Buffer` or `System.IO.Stream` (including subclasses) is not an escape: those APIs read or write the array without taking ownership. Without this, `stream.Read(buffer, 0, n)` and `buffer.AsSpan()` would silence the rule in exactly the code it exists for.
2. **ZA0304 and ZA0110 report at compilation end.** Their safety depends on every use of the symbol in the compilation. ZA0110 also has to rule out method-group conversions such as `Func<int[], int> f = Sum;`. Compilation-end diagnostics show up on build and in full-solution analysis, not live while typing. The rule docs say so.
3. **ZA0110 also skips** virtual, abstract and partial methods, async methods and iterators. A span parameter is not allowed in async methods or iterators, and the other three have signatures fixed by other declarations.
4. **ZA0111's code fix deconstructs.** `foreach (var key in map.Keys)` becomes `foreach (var (key, _) in map)`. The loop body does not change, and the fix is offered only when `KeyValuePair<TKey, TValue>.Deconstruct` exists.

---

### Task 1: ZA0303 — rented array never returned (#78)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/ReturnRentedArrayAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0303_ReturnRentedArrayTests.cs`
- Modify: `src/ZeroAlloc.Analyzers/DiagnosticIds.cs`, `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md`, `docs/rules/memory.md`, `docs/getting-started.md`

**Interfaces:**
- Produces: `DiagnosticIds.ReturnRentedArray = "ZA0303"`, and the `### New Rules` table in `AnalyzerReleases.Unshipped.md` that later tasks append rows to.

- [ ] **Step 1: Add the ID constant**

In `src/ZeroAlloc.Analyzers/DiagnosticIds.cs`, after `public const string UseArrayPool = "ZA0302";`:

```csharp
    public const string ReturnRentedArray = "ZA0303";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0303_ReturnRentedArrayTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0303_ReturnRentedArrayTests
{
    private static DiagnosticResult Expected(string local) =>
        CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .Diagnostic(DiagnosticIds.ReturnRentedArray)
            .WithLocation(0)
            .WithArguments(local);

    [Fact]
    public async Task RentedAndNeverReturned_Reports()
    {
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    buffer[0] = 1;
                    return buffer.Length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task RentedIntoExistingLocal_Reports()
    {
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    byte[] buffer;
                    buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    return buffer.Length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task PassedOnlyToSpanAndStreamApis_Reports()
    {
        // MemoryExtensions and Stream read or write the array without taking ownership.
        var source = """
            using System;
            using System.Buffers;
            using System.IO;

            class C
            {
                int M(Stream stream)
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    var read = stream.Read(buffer, 0, buffer.Length);
                    buffer.AsSpan(0, read).Clear();
                    return read;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task ReturnedInFinally_NoDiagnostic()
    {
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    try
                    {
                        return buffer.Length;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ReturnedOutsideFinally_NoDiagnostic()
    {
        // Letting the GC take the buffer on the exception path is accepted .NET guidance.
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    var length = buffer.Length;
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                    return length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Theory]
    [InlineData("return buffer;")]
    [InlineData("_field = buffer; return null;")]
    [InlineData("Keep(buffer); return null;")]
    [InlineData("var alias = buffer; return alias.Length;")]
    [InlineData("Func<int> length = () => buffer.Length; return length();")]
    public async Task ArrayEscapes_NoDiagnostic(string statements)
    {
        // Once the array leaves the method, ownership may have moved, so the rule stays silent.
        var source = $$"""
            using System;
            using System.Buffers;

            class C
            {
                private byte[] _field = Array.Empty<byte>();

                private static void Keep(byte[] array) { }

                object M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    {{statements}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task RentNotStoredInLocal_NoDiagnostic()
    {
        var source = """
            using System.Buffers;

            class C
            {
                private static void Keep(byte[] array) { }

                void M() => Keep(ArrayPool<byte>.Shared.Rent(16));
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0303"`
Expected: build error CS0246, `The type or namespace name 'ReturnRentedArrayAnalyzer' could not be found`.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/ReturnRentedArrayAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReturnRentedArrayAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ReturnRentedArray,
        "Return rented arrays to the pool",
        "Array rented into '{0}' is never returned to the pool",
        DiagnosticCategories.Memory,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // Methods on these types read or write a passed array without taking ownership of it.
    private static readonly string[] TrustedTypeNames =
    [
        "System.MemoryExtensions",
        "System.Array",
        "System.Buffer",
        "System.IO.Stream",
    ];

    private static readonly string[] SpanTypeNames =
    [
        "System.Span`1",
        "System.ReadOnlySpan`1",
        "System.Memory`1",
        "System.ReadOnlyMemory`1",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var arrayPool = start.Compilation.GetTypeByMetadataName("System.Buffers.ArrayPool`1");
            if (arrayPool is null)
                return;

            var known = new KnownTypes(
                arrayPool,
                Resolve(start.Compilation, TrustedTypeNames),
                Resolve(start.Compilation, SpanTypeNames));

            start.RegisterOperationAction(context => AnalyzeInvocation(context, known), OperationKind.Invocation);
        });
    }

    private static ImmutableArray<INamedTypeSymbol> Resolve(Compilation compilation, string[] metadataNames)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var name in metadataNames)
        {
            if (compilation.GetTypeByMetadataName(name) is { } type)
                builder.Add(type);
        }

        return builder.ToImmutable();
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes known)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod.Name != "Rent" || !known.IsArrayPool(invocation.TargetMethod.ContainingType))
            return;

        if (GetAssignedLocal(invocation) is not { } local)
            return;

        IOperation root = invocation;
        while (root.Parent is not null)
            root = root.Parent;

        var rentFunctions = EnclosingFunctions(invocation);

        foreach (var reference in root.Descendants().OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Local, local))
                continue;

            // Writing the local is not a use of the rented array.
            if (reference.Parent is ISimpleAssignmentOperation assignment && assignment.Target == reference)
                continue;

            if (IsReturnedOrEscapes(reference, rentFunctions, known))
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), local.Name));
    }

    private static ILocalSymbol? GetAssignedLocal(IInvocationOperation invocation) => invocation.Parent switch
    {
        IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } => declarator.Symbol,
        ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment
            when assignment.Value == invocation => target.Local,
        _ => null,
    };

    private static ImmutableArray<IOperation> EnclosingFunctions(IOperation operation)
    {
        var builder = ImmutableArray.CreateBuilder<IOperation>();
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                builder.Add(current);
        }

        return builder.ToImmutable();
    }

    // True when this use hands the array back to the pool, or lets it leave the method so
    // ownership may have moved. Either way the rule must stay silent.
    private static bool IsReturnedOrEscapes(
        ILocalReferenceOperation reference,
        ImmutableArray<IOperation> rentFunctions,
        KnownTypes known)
    {
        for (var current = reference.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation && !rentFunctions.Contains(current))
                return true;
        }

        switch (reference.Parent)
        {
            case IArgumentOperation { Parent: IInvocationOperation call } argument:
                if (call.TargetMethod.Name == "Return" && known.IsArrayPool(call.TargetMethod.ContainingType))
                    return true;
                return argument.Parameter?.RefKind != RefKind.None || !known.IsTrusted(call.TargetMethod.ContainingType);
            case IArgumentOperation { Parent: IObjectCreationOperation creation }:
                return !known.IsSpan(creation.Type);
            case IConversionOperation conversion:
                return conversion.Parent is not IForEachLoopOperation && !known.IsSpan(conversion.Type);
            case IArrayElementReferenceOperation element:
                return element.ArrayReference != reference;
            case IPropertyReferenceOperation property:
                return property.Instance != reference;
            case IInvocationOperation call:
                return call.Instance != reference;
            case IForEachLoopOperation:
                return false;
            default:
                return true;
        }
    }

    private sealed class KnownTypes(
        INamedTypeSymbol arrayPool,
        ImmutableArray<INamedTypeSymbol> trustedTypes,
        ImmutableArray<INamedTypeSymbol> spanTypes)
    {
        public bool IsArrayPool(ITypeSymbol? type) => InheritsFrom(type, arrayPool);

        public bool IsTrusted(ITypeSymbol? type) => trustedTypes.Any(trusted => InheritsFrom(type, trusted));

        public bool IsSpan(ITypeSymbol? type) =>
            type is INamedTypeSymbol named && spanTypes.Contains(named.OriginalDefinition, SymbolEqualityComparer.Default);

        private static bool InheritsFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
                    return true;
            }

            return false;
        }
    }
}
```

- [ ] **Step 5: Add the release-tracking row**

`src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md` holds only its two header comment lines. Append, keeping the file's CRLF line endings:

```
### New Rules

Rule ID | Category                | Severity | Notes
--------|-------------------------|----------|----------------------------------------
ZA0303  | Performance.Memory      | Warning  | ReturnRentedArrayAnalyzer
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0303"`
Expected: PASS, 11 tests.

- [ ] **Step 7: Document the rule**

Append to `docs/rules/memory.md` (CRLF, after the ZA0302 suppression block):

````markdown

---

## ZA0303 — Return rented arrays to the pool {#za0303}

> **Severity**: Warning | **Min TFM**: Any | **Code fix**: No

### Why

`ArrayPool<T>.Rent` only saves an allocation if the array goes back with `Return`. An array that is rented and never returned is a plain allocation plus pool bookkeeping, and the pool has to allocate again for the next caller. The rule reports a rented array that is stored in a local, never passed to `Return`, and never leaves the method.

It stays silent once the array may have changed owner: when it is returned, stored in a field, aliased, captured by a lambda, or passed to a method that could keep it. Passing it to `Stream`, `Array`, `Buffer` or `MemoryExtensions` methods such as `AsSpan` does not count, because those only read or write it. A `Return` outside a `finally` is fine: if an exception skips it, the GC collects the array.

### Before

```csharp
// ❌ the rented buffer is never returned, so every call allocates anyway
public int Checksum(Stream stream)
{
    var buffer = ArrayPool<byte>.Shared.Rent(4096);
    var read = stream.Read(buffer, 0, buffer.Length);
    return Crc32.Compute(buffer.AsSpan(0, read));
}
```

### After

```csharp
// ✓ the buffer goes back to the pool for the next caller
public int Checksum(Stream stream)
{
    var buffer = ArrayPool<byte>.Shared.Rent(4096);
    try
    {
        var read = stream.Read(buffer, 0, buffer.Length);
        return Crc32.Compute(buffer.AsSpan(0, read));
    }
    finally
    {
        ArrayPool<byte>.Shared.Return(buffer);
    }
}
```

### Suppression

```csharp
#pragma warning disable ZA0303
// or in .editorconfig: dotnet_diagnostic.ZA0303.severity = none
```
````

In `docs/getting-started.md`, add after the ZA0302 row in the Memory table:

```markdown
| [ZA0303](rules/memory.md#za0303) | Return rented arrays to the pool | Warning | Any |
```

- [ ] **Step 8: Commit**

```bash
git add src/ZeroAlloc.Analyzers tests/ZeroAlloc.Analyzers.Tests/ZA0303_ReturnRentedArrayTests.cs docs/rules/memory.md docs/getting-started.md
git commit -m "feat: add ZA0303 to report rented arrays never returned to the pool" -m "Closes #78" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: ZA0210 — use a u8 literal (#75)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/UseUtf8StringLiteralAnalyzer.cs`
- Create: `src/ZeroAlloc.Analyzers.CodeFixes/UseUtf8StringLiteralCodeFixProvider.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0210_UseUtf8StringLiteralTests.cs`
- Modify: `tests/ZeroAlloc.Analyzers.Tests/Verifiers/CSharpAnalyzerVerifier.cs`, `src/ZeroAlloc.Analyzers/DiagnosticIds.cs`, `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md`, `docs/rules/strings.md`, `docs/getting-started.md`

**Interfaces:**
- Consumes: the Unshipped `### New Rules` table from Task 1.
- Produces: `CSharpAnalyzerVerifier<TAnalyzer>.VerifyAnalyzerAsync(string source, LanguageVersion languageVersion, params DiagnosticResult[] expected)`. It analyzes with .NET 8 reference assemblies at the given language version. Task 7 uses it.

- [ ] **Step 1: Add the ID constant**

In `DiagnosticIds.cs`, after `AvoidValueTypeBoxingInStringConcat = "ZA0209";`:

```csharp
    public const string UseUtf8StringLiteral = "ZA0210";
```

- [ ] **Step 2: Add the language-version overload to the verifier**

Replace the whole of `tests/ZeroAlloc.Analyzers.Tests/Verifiers/CSharpAnalyzerVerifier.cs` with this. The existing public overloads keep their behavior.

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace ZeroAlloc.Analyzers.Tests.Verifiers;

public static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(
        string source,
        string targetFramework = "net8.0",
        params DiagnosticResult[] expected)
    {
        await RunAsync(source, targetFramework, ReferenceAssemblies.Net.Net80, languageVersion: null, expected);
    }

    public static async Task VerifyAnalyzerAsync(
        string source,
        string targetFramework,
        ReferenceAssemblies referenceAssemblies,
        params DiagnosticResult[] expected)
    {
        await RunAsync(source, targetFramework, referenceAssemblies, languageVersion: null, expected);
    }

    /// <summary>Analyzes with .NET 8 reference assemblies at the given C# language version.</summary>
    public static async Task VerifyAnalyzerAsync(
        string source,
        LanguageVersion languageVersion,
        params DiagnosticResult[] expected)
    {
        await RunAsync(source, "net8.0", ReferenceAssemblies.Net.Net80, languageVersion, expected);
    }

    public static async Task VerifyNoDiagnosticAsync(
        string source,
        string targetFramework = "net8.0")
    {
        await VerifyAnalyzerAsync(source, targetFramework);
    }

    public static async Task VerifyNoDiagnosticAsync(
        string source,
        string targetFramework,
        ReferenceAssemblies referenceAssemblies)
    {
        await VerifyAnalyzerAsync(source, targetFramework, referenceAssemblies);
    }

    private static async Task RunAsync(
        string source,
        string targetFramework,
        ReferenceAssemblies referenceAssemblies,
        LanguageVersion? languageVersion,
        DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = referenceAssemblies,
        };

        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", $"""
                is_global = true
                build_property.TargetFramework = {targetFramework}
                """));

        if (languageVersion is { } version)
        {
            test.SolutionTransforms.Add((solution, projectId) =>
            {
                var parseOptions = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
                return solution.WithProjectParseOptions(projectId, parseOptions.WithLanguageVersion(version));
            });
        }

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
```

- [ ] **Step 3: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0210_UseUtf8StringLiteralTests.cs`:

```csharp
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0210_UseUtf8StringLiteralTests
{
    private static DiagnosticResult Expected(string literal) =>
        CSharpAnalyzerVerifier<UseUtf8StringLiteralAnalyzer>
            .Diagnostic(DiagnosticIds.UseUtf8StringLiteral)
            .WithLocation(0)
            .WithArguments(literal);

    [Fact]
    public async Task ConstantPassedAsSpan_ReportsAndFixes()
    {
        var source = """
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                void M() => Write({|#0:Encoding.UTF8.GetBytes("hello")|});
            }
            """;

        var fixedSource = """
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                void M() => Write("hello"u8);
            }
            """;

        await CSharpCodeFixVerifier<UseUtf8StringLiteralAnalyzer, UseUtf8StringLiteralCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("\"hello\""));
    }

    [Fact]
    public async Task ConstantAssignedToSpanLocal_ReportsAndFixes()
    {
        var source = """
            using System;
            using System.Text;

            class C
            {
                int M()
                {
                    ReadOnlySpan<byte> header = {|#0:Encoding.UTF8.GetBytes("GET ")|};
                    return header.Length;
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.Text;

            class C
            {
                int M()
                {
                    ReadOnlySpan<byte> header = "GET "u8;
                    return header.Length;
                }
            }
            """;

        await CSharpCodeFixVerifier<UseUtf8StringLiteralAnalyzer, UseUtf8StringLiteralCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("\"GET \""));
    }

    [Fact]
    public async Task CSharp12_Reports()
    {
        var source = """
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                void M() => Write({|#0:Encoding.UTF8.GetBytes("hello")|});
            }
            """;

        await CSharpAnalyzerVerifier<UseUtf8StringLiteralAnalyzer>
            .VerifyAnalyzerAsync(source, LanguageVersion.CSharp12, Expected("\"hello\""));
    }

    [Fact]
    public async Task CSharp10_NoDiagnostic()
    {
        // u8 literals arrived in C# 11.
        var source = """
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                void M() => Write(Encoding.UTF8.GetBytes("hello"));
            }
            """;

        await CSharpAnalyzerVerifier<UseUtf8StringLiteralAnalyzer>
            .VerifyAnalyzerAsync(source, LanguageVersion.CSharp10);
    }

    [Theory]
    [InlineData("byte[] bytes = Encoding.UTF8.GetBytes(\"hello\"); Use(bytes);")]
    [InlineData("Write(Encoding.UTF8.GetBytes(name));")]
    [InlineData("Write(Encoding.ASCII.GetBytes(\"hello\"));")]
    [InlineData("Write(Encoding.UTF8.GetBytes(\"\\uD800\"));")]
    public async Task NotADropInReplacement_NoDiagnostic(string statement)
    {
        // A byte[] result, a non-constant, another encoding, or text with a lone surrogate.
        var source = $$"""
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                static void Use(byte[] bytes) { }

                void M(string name)
                {
                    {{statement}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseUtf8StringLiteralAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0210"`
Expected: build error CS0246 for `UseUtf8StringLiteralAnalyzer`.

- [ ] **Step 5: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/UseUtf8StringLiteralAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseUtf8StringLiteralAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseUtf8StringLiteral,
        "Use a UTF-8 string literal",
        "Use the UTF-8 literal {0}u8 instead of encoding a constant string on every call",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation is not CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp11 })
                return;

            var encoding = start.Compilation.GetTypeByMetadataName("System.Text.Encoding");
            var readOnlySpan = start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            if (encoding is null || readOnlySpan is null)
                return;

            start.RegisterOperationAction(
                context => AnalyzeInvocation(context, encoding, readOnlySpan),
                OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, INamedTypeSymbol encoding, INamedTypeSymbol readOnlySpan)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name != "GetBytes"
            || method.Parameters.Length != 1
            || method.Parameters[0].Type.SpecialType != SpecialType.System_String
            || !SymbolEqualityComparer.Default.Equals(method.ContainingType, encoding))
        {
            return;
        }

        if (invocation.Instance is not IPropertyReferenceOperation { Property: { Name: "UTF8", IsStatic: true } property }
            || !SymbolEqualityComparer.Default.Equals(property.ContainingType, encoding))
        {
            return;
        }

        if (invocation.Arguments[0].Value.ConstantValue is not { HasValue: true, Value: string text } || !IsWellFormedUtf16(text))
            return;

        // Only a result that becomes a ReadOnlySpan<byte> can take the literal unchanged.
        // Kept as a byte[], the literal would need .ToArray() and gains nothing.
        if (invocation.Parent is not IConversionOperation { IsImplicit: true, Type: INamedTypeSymbol target }
            || !SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, readOnlySpan)
            || target.TypeArguments[0].SpecialType != SpecialType.System_Byte)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            SymbolDisplay.FormatLiteral(text, quote: true)));
    }

    // A u8 literal does not compile for text with an unpaired surrogate.
    private static bool IsWellFormedUtf16(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return false;
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return false;
            }
        }

        return true;
    }
}
```

- [ ] **Step 6: Write the code fix**

Create `src/ZeroAlloc.Analyzers.CodeFixes/UseUtf8StringLiteralCodeFixProvider.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class UseUtf8StringLiteralCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.UseUtf8StringLiteral];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root?.FindNode(context.Diagnostics[0].Location.SourceSpan) is not InvocationExpressionSyntax invocation)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use a UTF-8 string literal",
                ct => ReplaceAsync(context.Document, invocation, ct),
                equivalenceKey: DiagnosticIds.UseUtf8StringLiteral),
            context.Diagnostics[0]);
    }

    private static async Task<Document> ReplaceAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        var model = await document.GetSemanticModelAsync(ct);
        var root = await document.GetSyntaxRootAsync(ct);
        if (model is null || root is null)
            return document;

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        if (model.GetConstantValue(argument, ct) is not { HasValue: true, Value: string text })
            return document;

        var literal = SyntaxFactory.ParseExpression(SymbolDisplay.FormatLiteral(text, quote: true) + "u8")
            .WithTriviaFrom(invocation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, literal));
    }
}
```

- [ ] **Step 7: Add the release-tracking row**

Append to the `### New Rules` table in `AnalyzerReleases.Unshipped.md`:

```
ZA0210  | Performance.Strings     | Warning  | UseUtf8StringLiteralAnalyzer
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release`
Expected: PASS, including the existing tests, which go through the refactored verifier.

- [ ] **Step 9: Document the rule**

Append to `docs/rules/strings.md`:

````markdown

---

## ZA0210 — Use a UTF-8 string literal {#za0210}

> **Severity**: Warning | **Min TFM**: Any (C# 11) | **Code fix**: Yes

### Why

`Encoding.UTF8.GetBytes("constant")` allocates a new array and encodes the same text on every call. Since C# 11, a `"constant"u8` literal is a `ReadOnlySpan<byte>` that points at bytes the compiler stores in the assembly, so it costs nothing at run time. The rule only reports calls whose result is used as a `ReadOnlySpan<byte>`, so the literal is a drop-in replacement. Kept as a `byte[]`, the literal would need `.ToArray()` and gains nothing.

### Before

```csharp
// ❌ allocates and encodes "\r\n" on every write
writer.Write(Encoding.UTF8.GetBytes("\r\n"));
```

### After

```csharp
// ✓ static data, no allocation
writer.Write("\r\n"u8);
```

### Suppression

```csharp
#pragma warning disable ZA0210
// or in .editorconfig: dotnet_diagnostic.ZA0210.severity = none
```
````

In `docs/getting-started.md`, add after the ZA0209 row:

```markdown
| [ZA0210](rules/strings.md#za0210) | Use a UTF-8 string literal | Warning | Any (C# 11) |
```

- [ ] **Step 10: Commit**

```bash
git add src tests docs/rules/strings.md docs/getting-started.md
git commit -m "feat: add ZA0210 to replace constant UTF-8 encoding with u8 literals" -m "Closes #75" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: ZA0304 — constant lookup table as a ReadOnlySpan property (#79)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/ReadOnlySpanUse.cs`
- Create: `src/ZeroAlloc.Analyzers/Analyzers/UseReadOnlySpanForConstantTableAnalyzer.cs`
- Create: `src/ZeroAlloc.Analyzers.CodeFixes/UsingDirectives.cs`
- Create: `src/ZeroAlloc.Analyzers.CodeFixes/UseReadOnlySpanForConstantTableCodeFixProvider.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0304_UseReadOnlySpanForConstantTableTests.cs`
- Modify: `src/ZeroAlloc.Analyzers.CodeFixes/AvoidZeroLengthArrayAllocationCodeFixProvider.cs`, `DiagnosticIds.cs`, `AnalyzerReleases.Unshipped.md`, `docs/rules/memory.md`, `docs/getting-started.md`

**Interfaces:**
- Produces:
  - `internal static class ReadOnlySpanUse` with `public static bool IsCompatible(IOperation arrayReference, INamedTypeSymbol readOnlySpan)`. It returns true when the use of the array would keep compiling with the same meaning if the array were a `ReadOnlySpan<T>`. Task 7 uses it.
  - `internal static class UsingDirectives` in `ZeroAlloc.Analyzers.CodeFixes` with `public static SyntaxNode EnsureSystem(SyntaxNode root)`. It returns the root with `using System;` added unless that using is already there. Task 7 uses it.

- [ ] **Step 1: Add the ID constant**

In `DiagnosticIds.cs`, after `ReturnRentedArray = "ZA0303";`:

```csharp
    public const string UseReadOnlySpanForConstantTable = "ZA0304";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0304_UseReadOnlySpanForConstantTableTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0304_UseReadOnlySpanForConstantTableTests
{
    private static DiagnosticResult Expected(string field, string elementType) =>
        CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .Diagnostic(DiagnosticIds.UseReadOnlySpanForConstantTable)
            .WithLocation(0)
            .WithArguments(field, elementType);

    [Fact]
    public async Task ByteTable_ReportsAndFixes()
    {
        var source = """
            using System;

            class C
            {
                private static readonly byte[] {|#0:Table|} = { 1, 2, 3 };

                int M(int i) => Table[i] + Table.Length;
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                private static ReadOnlySpan<byte> Table => [1, 2, 3];

                int M(int i) => Table[i] + Table.Length;
            }
            """;

        await CSharpCodeFixVerifier<UseReadOnlySpanForConstantTableAnalyzer, UseReadOnlySpanForConstantTableCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Table", "byte"));
    }

    [Fact]
    public async Task IntTableWithArrayCreationAndForeach_ReportsAndFixes()
    {
        var source = """
            using System;

            class C
            {
                internal static readonly int[] {|#0:Primes|} = new int[] { 2, 3, 5 };

                int Sum()
                {
                    var total = 0;
                    foreach (var prime in Primes)
                        total += prime;
                    return total;
                }
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                internal static ReadOnlySpan<int> Primes => [2, 3, 5];

                int Sum()
                {
                    var total = 0;
                    foreach (var prime in Primes)
                        total += prime;
                    return total;
                }
            }
            """;

        await CSharpCodeFixVerifier<UseReadOnlySpanForConstantTableAnalyzer, UseReadOnlySpanForConstantTableCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Primes", "int"));
    }

    [Fact]
    public async Task IntTableOnNet6_NoDiagnostic()
    {
        // Wider primitives come from static data only once RuntimeHelpers.CreateSpan exists, in .NET 7.
        var source = """
            class C
            {
                private static readonly int[] Table = { 1, 2, 3 };

                int M(int i) => Table[i];
            }
            """;

        await CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net6.0", Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net60);
    }

    [Fact]
    public async Task ByteTableOnNet6_Reports()
    {
        var source = """
            class C
            {
                private static readonly byte[] {|#0:Table|} = { 1, 2, 3 };

                int M(int i) => Table[i];
            }
            """;

        await CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .VerifyAnalyzerAsync(source, "net6.0", Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net60, Expected("Table", "byte"));
    }

    [Theory]
    [InlineData("private static readonly string[] Table = { \"a\" };", "int M() => Table.Length;")]
    [InlineData("public static readonly byte[] Table = { 1 };", "int M() => Table.Length;")]
    [InlineData("private static readonly byte[] Table = { 1, Seed };", "int M() => Table.Length;")]
    [InlineData("private static readonly byte[] Table = { 1 };", "void M() => Table[0] = 2;")]
    [InlineData("private static readonly byte[] Table = { 1 };", "void M() => Use(Table);")]
    [InlineData("private static readonly byte[] Table = { 1 };", "int M() => Table.ToList().Count;")]
    [InlineData("private static readonly byte[] Table = { 1 }, Other = { 2 };", "int M() => Table.Length + Other.Length;")]
    public async Task NotAConstantReadOnlyTable_NoDiagnostic(string field, string member)
    {
        // Non-primitive elements, exposed fields, non-constant elements, writes, passing the
        // array on, LINQ, and multi-variable declarations all keep the array.
        var source = $$"""
            using System.Linq;

            class C
            {
                private static readonly byte Seed = 2;

                {{field}}

                private static void Use(byte[] array) { }

                {{member}}
            }
            """;

        await CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0304"`
Expected: build errors CS0246 for `UseReadOnlySpanForConstantTableAnalyzer` and `UseReadOnlySpanForConstantTableCodeFixProvider`.

- [ ] **Step 4: Write the shared span-compatibility helper**

Create `src/ZeroAlloc.Analyzers/ReadOnlySpanUse.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

internal static class ReadOnlySpanUse
{
    /// <summary>
    /// True when this use of an array would keep compiling with the same meaning if the array
    /// became a <c>ReadOnlySpan&lt;T&gt;</c>: element reads, <c>Length</c>, <c>foreach</c>, and
    /// implicit conversion to <c>ReadOnlySpan&lt;T&gt;</c>.
    /// </summary>
    public static bool IsCompatible(IOperation arrayReference, INamedTypeSymbol readOnlySpan)
    {
        switch (arrayReference.Parent)
        {
            case IArrayElementReferenceOperation element when element.ArrayReference == arrayReference:
                return element.Indices.All(index => index.Type?.SpecialType == SpecialType.System_Int32)
                    && !IsWrittenOrMutated(element);
            case IPropertyReferenceOperation { Property.Name: "Length" } length when length.Instance == arrayReference:
                return true;
            case IForEachLoopOperation loop:
                return loop.Collection == arrayReference;
            case IConversionOperation { IsImplicit: true } conversion:
                return conversion.Parent is IForEachLoopOperation loop && loop.Collection == conversion
                    || conversion.Type is INamedTypeSymbol target
                        && SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, readOnlySpan);
            default:
                return false;
        }
    }

    // A ReadOnlySpan<T> element is readonly, so writes, ref access and mutating calls on
    // mutable struct elements stop compiling or start working on a defensive copy.
    private static bool IsWrittenOrMutated(IArrayElementReferenceOperation element)
    {
        if (element.Syntax.Parent is RefExpressionSyntax)
            return true;

        switch (element.Parent)
        {
            case ISimpleAssignmentOperation assignment when assignment.Target == element:
            case ICompoundAssignmentOperation compound when compound.Target == element:
            case ICoalesceAssignmentOperation coalesce when coalesce.Target == element:
            case IIncrementOrDecrementOperation:
            case IAddressOfOperation:
            case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out }:
                return true;
        }

        return element.Type is { IsValueType: true, IsReadOnly: false, SpecialType: SpecialType.None, TypeKind: not TypeKind.Enum }
            && element.Parent is IMemberReferenceOperation or IInvocationOperation;
    }
}
```

- [ ] **Step 5: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/UseReadOnlySpanForConstantTableAnalyzer.cs`:

```csharp
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseReadOnlySpanForConstantTableAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseReadOnlySpanForConstantTable,
        "Use a ReadOnlySpan<T> property for constant lookup tables",
        "Lookup table '{0}' can be a 'static ReadOnlySpan<{1}>' property that reads constant data without allocating",
        DiagnosticCategories.Memory,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var readOnlySpan = start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            if (readOnlySpan is null)
                return;

            // .NET 7+: constant ReadOnlySpan<T> data of wider primitives is read through RuntimeHelpers.CreateSpan.
            var hasCreateSpan = start.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.RuntimeHelpers")
                ?.GetMembers("CreateSpan").Length > 0;

            var candidates = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
            var disqualified = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterOperationAction(context =>
            {
                var initializer = (IFieldInitializerOperation)context.Operation;
                if (initializer.InitializedFields.Length == 1
                    && IsCandidateField(initializer.InitializedFields[0], hasCreateSpan)
                    && IsConstantArray(initializer.Value))
                {
                    candidates[initializer.InitializedFields[0]] = true;
                }
            }, OperationKind.FieldInitializer);

            start.RegisterOperationAction(context =>
            {
                var reference = (IFieldReferenceOperation)context.Operation;
                if (reference.Field.IsStatic && reference.Field.Type is IArrayTypeSymbol
                    && !ReadOnlySpanUse.IsCompatible(reference, readOnlySpan))
                {
                    disqualified[reference.Field.OriginalDefinition] = true;
                }
            }, OperationKind.FieldReference);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (var field in candidates.Keys)
                {
                    if (disqualified.ContainsKey(field))
                        continue;

                    var elementType = ((IArrayTypeSymbol)field.Type).ElementType;
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        field.Locations[0],
                        field.Name,
                        elementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }
            });
        });
    }

    private static bool IsCandidateField(IFieldSymbol field, bool hasCreateSpan)
    {
        if (!field.IsStatic || !field.IsReadOnly || field.IsConst
            || field.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Internal)
            || field.Type is not IArrayTypeSymbol { Rank: 1 } array
            || field.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        if (field.DeclaringSyntaxReferences[0].GetSyntax() is
            Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax
            {
                Parent: Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax { Variables.Count: > 1 }
            })
        {
            return false;
        }

        if (!field.GetAttributes().IsEmpty)
            return false;

        return array.ElementType.SpecialType switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => true,
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double => hasCreateSpan,
            _ => false,
        };
    }

    private static bool IsConstantArray(IOperation value)
    {
        while (value is IConversionOperation { IsImplicit: true } conversion)
            value = conversion.Operand;

        var elements = value switch
        {
            IArrayCreationOperation { Initializer: { } initializer } => initializer.ElementValues,
            ICollectionExpressionOperation collection => collection.Elements,
            _ => default,
        };

        return !elements.IsDefaultOrEmpty && elements.All(element => element.ConstantValue.HasValue);
    }
}
```

- [ ] **Step 6: Extract the shared using-directive helper**

Create `src/ZeroAlloc.Analyzers.CodeFixes/UsingDirectives.cs`. This moves the existing logic out of `AvoidZeroLengthArrayAllocationCodeFixProvider` unchanged:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

internal static class UsingDirectives
{
    /// <summary>Returns <paramref name="root"/> with <c>using System;</c> added unless the file or a namespace already has it.</summary>
    public static SyntaxNode EnsureSystem(SyntaxNode root)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        var hasSystemUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "System")
            || compilationUnit.Members.OfType<BaseNamespaceDeclarationSyntax>()
                .Any(ns => ns.Usings.Any(u => u.Name?.ToString() == "System"));
        if (hasSystemUsing)
            return root;

        // Detect the document's line ending style from existing trivia to stay platform-neutral
        var eol = root.DescendantTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia))
            .ToFullString();
        if (string.IsNullOrEmpty(eol)) eol = "\n";

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("System"))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(eol), SyntaxFactory.EndOfLine(eol));
        return compilationUnit.AddUsings(usingDirective);
    }
}
```

In `AvoidZeroLengthArrayAllocationCodeFixProvider.cs`, replace everything from `var newRoot = root.ReplaceNode(arrayCreation, arrayEmptyCall);` to `return document.WithSyntaxRoot(newRoot);` with:

```csharp
        var newRoot = UsingDirectives.EnsureSystem(root.ReplaceNode(arrayCreation, arrayEmptyCall));
        return document.WithSyntaxRoot(newRoot);
```

The old code detected the line ending from the root before the replacement. The replacement doesn't touch line endings, so detecting it from the new root gives the same result.

- [ ] **Step 7: Write the code fix**

Create `src/ZeroAlloc.Analyzers.CodeFixes/UseReadOnlySpanForConstantTableCodeFixProvider.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class UseReadOnlySpanForConstantTableCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.UseReadOnlySpanForConstantTable];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var declarator = root?.FindNode(context.Diagnostics[0].Location.SourceSpan)
            .FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        if (declarator?.Parent?.Parent is not FieldDeclarationSyntax field
            || field.Declaration.Type is not ArrayTypeSyntax arrayType
            || GetElements(declarator.Initializer?.Value) is not { } elements)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Make it a ReadOnlySpan<T> property",
                ct => ReplaceAsync(context.Document, field, declarator, arrayType, elements, ct),
                equivalenceKey: DiagnosticIds.UseReadOnlySpanForConstantTable),
            context.Diagnostics[0]);
    }

    private static IEnumerable<ExpressionSyntax>? GetElements(ExpressionSyntax? value) => value switch
    {
        InitializerExpressionSyntax initializer => initializer.Expressions,
        ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
        ImplicitArrayCreationExpressionSyntax creation => creation.Initializer.Expressions,
        CollectionExpressionSyntax collection when collection.Elements.All(e => e is ExpressionElementSyntax) =>
            collection.Elements.Cast<ExpressionElementSyntax>().Select(e => e.Expression),
        _ => null,
    };

    private static async Task<Document> ReplaceAsync(
        Document document,
        FieldDeclarationSyntax field,
        VariableDeclaratorSyntax declarator,
        ArrayTypeSyntax arrayType,
        IEnumerable<ExpressionSyntax> elements,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var modifiers = string.Join(" ", field.Modifiers
            .Where(m => !m.IsKind(SyntaxKind.ReadOnlyKeyword))
            .Select(m => m.Text));
        var values = string.Join(", ", elements.Select(e => e.ToString()));
        var property = SyntaxFactory.ParseMemberDeclaration(
            $"{modifiers} ReadOnlySpan<{arrayType.ElementType}> {declarator.Identifier.Text} => [{values}];")!
            .WithTriviaFrom(field);

        return document.WithSyntaxRoot(UsingDirectives.EnsureSystem(root.ReplaceNode(field, property)));
    }
}
```

- [ ] **Step 8: Add the release-tracking row**

Append to the Unshipped table:

```
ZA0304  | Performance.Memory      | Info     | UseReadOnlySpanForConstantTableAnalyzer
```

- [ ] **Step 9: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release`
Expected: PASS. The ZA0109 code-fix tests also pass, now through `UsingDirectives`.

- [ ] **Step 10: Document the rule**

Append to `docs/rules/memory.md`:

````markdown

---

## ZA0304 — Use a ReadOnlySpan\<T\> property for constant lookup tables {#za0304}

> **Severity**: Info | **Min TFM**: Any (wider primitives: net7.0) | **Code fix**: Yes

### Why

`static readonly int[] Table = { … }` allocates an array when the type is initialized and copies the constants into it. A `static ReadOnlySpan<int> Table => [ … ];` property reads them straight from the assembly's data section: no allocation, and NativeAOT can pre-initialize it. Call sites that index the table, read `Length` or `foreach` over it compile unchanged.

The rule reports `private` and `internal` tables of primitives whose every use is a read. `byte`, `sbyte` and `bool` tables qualify on every runtime; other primitives need `RuntimeHelpers.CreateSpan`, which arrived in .NET 7. Because it has to see every use of the field, the rule reports when the whole project is analyzed, on build or with full-solution analysis, not while you type.

### Before

```csharp
// ❌ allocated and filled at type initialization
private static readonly byte[] HexDigits = { 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 65, 66, 67, 68, 69, 70 };
```

### After

```csharp
// ✓ read from the assembly's static data, no allocation
private static ReadOnlySpan<byte> HexDigits => [48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 65, 66, 67, 68, 69, 70];
```

### Suppression

```csharp
#pragma warning disable ZA0304
// or in .editorconfig: dotnet_diagnostic.ZA0304.severity = none
```
````

In `docs/getting-started.md`, add after the ZA0303 row:

```markdown
| [ZA0304](rules/memory.md#za0304) | Use a ReadOnlySpan\<T\> property for constant lookup tables | Info | Any |
```

- [ ] **Step 11: Commit**

```bash
git add src tests docs/rules/memory.md docs/getting-started.md
git commit -m "feat: add ZA0304 to turn constant lookup tables into ReadOnlySpan properties" -m "Closes #79" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: ZA0111 — enumerate a ConcurrentDictionary, not its snapshot (#74)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/AvoidConcurrentDictionarySnapshotAnalyzer.cs`
- Create: `src/ZeroAlloc.Analyzers.CodeFixes/AvoidConcurrentDictionarySnapshotCodeFixProvider.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0111_AvoidConcurrentDictionarySnapshotTests.cs`
- Modify: `DiagnosticIds.cs`, `AnalyzerReleases.Unshipped.md`, `docs/rules/collections.md`, `docs/getting-started.md`

**Interfaces:**
- Consumes: the Unshipped table.
- Produces: `DiagnosticIds.AvoidConcurrentDictionarySnapshot = "ZA0111"`.

- [ ] **Step 1: Add the ID constant**

After `AvoidZeroLengthArrayAllocation = "ZA0109";`:

```csharp
    public const string AvoidConcurrentDictionarySnapshot = "ZA0111";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0111_AvoidConcurrentDictionarySnapshotTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0111_AvoidConcurrentDictionarySnapshotTests
{
    private static DiagnosticResult Expected(string access, string member) =>
        CSharpAnalyzerVerifier<AvoidConcurrentDictionarySnapshotAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidConcurrentDictionarySnapshot)
            .WithLocation(0)
            .WithArguments(access, member);

    [Fact]
    public async Task ForeachOverKeys_ReportsAndFixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                void M(ConcurrentDictionary<int, string> map)
                {
                    foreach (var key in {|#0:map.Keys|})
                    {
                        System.Console.WriteLine(key);
                    }
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                void M(ConcurrentDictionary<int, string> map)
                {
                    foreach (var (key, _) in map)
                    {
                        System.Console.WriteLine(key);
                    }
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidConcurrentDictionarySnapshotAnalyzer, AvoidConcurrentDictionarySnapshotCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("map.Keys", "Key"));
    }

    [Fact]
    public async Task ForeachOverValuesWithExplicitType_ReportsAndFixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                int M(ConcurrentDictionary<int, string> map)
                {
                    var total = 0;
                    foreach (string value in {|#0:map.Values|})
                        total += value.Length;
                    return total;
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                int M(ConcurrentDictionary<int, string> map)
                {
                    var total = 0;
                    foreach (var (_, value) in map)
                        total += value.Length;
                    return total;
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidConcurrentDictionarySnapshotAnalyzer, AvoidConcurrentDictionarySnapshotCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("map.Values", "Value"));
    }

    [Fact]
    public async Task LinqOverValues_Reports()
    {
        var source = """
            using System.Collections.Concurrent;
            using System.Linq;

            class C
            {
                int M(ConcurrentDictionary<int, string> map) => {|#0:map.Values|}.Count(v => v.Length > 0);
            }
            """;

        await CSharpAnalyzerVerifier<AvoidConcurrentDictionarySnapshotAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("map.Values", "Value"));
    }

    [Theory]
    [InlineData("var snapshot = map.Keys; return snapshot.Count;")]
    [InlineData("return map.Keys.Count;")]
    [InlineData("var total = 0; foreach (var key in plain.Keys) total += key; return total;")]
    public async Task NotEnumeratingTheSnapshot_NoDiagnostic(string statements)
    {
        // A stored snapshot may be intentional, Count is not an enumeration, and Dictionary.Keys is a cheap view.
        var source = $$"""
            using System.Collections.Concurrent;
            using System.Collections.Generic;

            class C
            {
                int M(ConcurrentDictionary<int, string> map, Dictionary<int, string> plain)
                {
                    {{statements}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidConcurrentDictionarySnapshotAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0111"`
Expected: build errors CS0246 for the analyzer and code fix types.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/AvoidConcurrentDictionarySnapshotAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidConcurrentDictionarySnapshotAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidConcurrentDictionarySnapshot,
        "Enumerate the ConcurrentDictionary instead of its Keys or Values",
        "'{0}' copies the dictionary under all locks; enumerate the dictionary and use '.{1}' instead",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var concurrentDictionary = start.Compilation.GetTypeByMetadataName("System.Collections.Concurrent.ConcurrentDictionary`2");
            if (concurrentDictionary is null)
                return;

            var enumerable = start.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            start.RegisterOperationAction(
                context => AnalyzePropertyReference(context, concurrentDictionary, enumerable),
                OperationKind.PropertyReference);
        });
    }

    private static void AnalyzePropertyReference(
        OperationAnalysisContext context,
        INamedTypeSymbol concurrentDictionary,
        INamedTypeSymbol? enumerable)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (reference.Property.Name is not ("Keys" or "Values")
            || !SymbolEqualityComparer.Default.Equals(reference.Property.ContainingType.OriginalDefinition, concurrentDictionary))
        {
            return;
        }

        IOperation use = reference;
        while (use.Parent is IConversionOperation { IsImplicit: true } conversion)
            use = conversion;

        var enumerated = use.Parent switch
        {
            IForEachLoopOperation loop => loop.Collection == use,
            IArgumentOperation { Parent: IInvocationOperation call } argument =>
                enumerable is not null
                && call.TargetMethod.IsExtensionMethod
                && SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, enumerable)
                && argument.Parameter?.Ordinal == 0,
            _ => false,
        };

        if (!enumerated)
            return;

        var member = reference.Property.Name == "Keys" ? "Key" : "Value";
        context.ReportDiagnostic(Diagnostic.Create(Rule, reference.Syntax.GetLocation(), reference.Syntax.ToString(), member));
    }
}
```

- [ ] **Step 5: Write the code fix**

Create `src/ZeroAlloc.Analyzers.CodeFixes/AvoidConcurrentDictionarySnapshotCodeFixProvider.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AvoidConcurrentDictionarySnapshotCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.AvoidConcurrentDictionarySnapshot];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken);
        if (root?.FindNode(context.Diagnostics[0].Location.SourceSpan) is not MemberAccessExpressionSyntax access
            || access.Parent is not ForEachStatementSyntax loop
            || loop.Expression != access
            || model is null
            || !CanDeconstruct(model, loop, access))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Enumerate the dictionary",
                ct => ReplaceAsync(context.Document, loop, access, ct),
                equivalenceKey: DiagnosticIds.AvoidConcurrentDictionarySnapshot),
            context.Diagnostics[0]);
    }

    // The fix deconstructs KeyValuePair, so the loop variable must keep its type and Deconstruct must exist.
    private static bool CanDeconstruct(SemanticModel model, ForEachStatementSyntax loop, MemberAccessExpressionSyntax access)
    {
        if (loop.Identifier.Text == "_")
            return false;

        var keyValuePair = model.Compilation.GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2");
        if (keyValuePair is null || keyValuePair.GetMembers("Deconstruct").IsEmpty)
            return false;

        if (loop.Type.IsVar)
            return true;

        var elementType = (model.GetTypeInfo(access).Type as INamedTypeSymbol)?.AllInterfaces
            .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            ?.TypeArguments[0];
        return SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(loop.Type).Type, elementType);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        ForEachStatementSyntax loop,
        MemberAccessExpressionSyntax access,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var name = loop.Identifier.Text;
        var designation = access.Name.Identifier.Text == "Keys" ? $"var ({name}, _)" : $"var (_, {name})";
        var variable = SyntaxFactory.ParseExpression(designation)
            .WithLeadingTrivia(loop.Type.GetLeadingTrivia())
            .WithTrailingTrivia(loop.Identifier.TrailingTrivia);

        var replacement = SyntaxFactory.ForEachVariableStatement(
            loop.AttributeLists,
            loop.AwaitKeyword,
            loop.ForEachKeyword,
            loop.OpenParenToken,
            variable,
            loop.InKeyword,
            access.Expression.WithTriviaFrom(access),
            loop.CloseParenToken,
            loop.Statement);

        return document.WithSyntaxRoot(root.ReplaceNode(loop, replacement));
    }
}
```

- [ ] **Step 6: Add the release-tracking row**

```
ZA0111  | Performance.Collections | Info     | AvoidConcurrentDictionarySnapshotAnalyzer
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0111"`
Expected: PASS, 6 tests.

- [ ] **Step 8: Document the rule**

Append to `docs/rules/collections.md`:

````markdown

---

## ZA0111 — Enumerate the ConcurrentDictionary instead of its Keys or Values {#za0111}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: Yes

### Why

Every read of `ConcurrentDictionary<TKey, TValue>.Keys` or `.Values` takes every lock in the dictionary and copies the contents into a new `ReadOnlyCollection`. Enumerating the dictionary itself takes no locks and copies nothing. The rule reports `Keys` or `Values` used directly by `foreach` or a LINQ call. A snapshot stored in a variable is left alone, because a consistent copy may be what you want. The code fix deconstructs the pair, so the loop body stays the same.

### Before

```csharp
// ❌ locks the whole dictionary and copies every key
foreach (var sessionId in _sessions.Keys)
    Expire(sessionId);
```

### After

```csharp
// ✓ lock-free enumeration, no copy
foreach (var (sessionId, _) in _sessions)
    Expire(sessionId);
```

### Suppression

```csharp
#pragma warning disable ZA0111
// or in .editorconfig: dotnet_diagnostic.ZA0111.severity = none
```
````

In `docs/getting-started.md`, add after the ZA0109 row:

```markdown
| [ZA0111](rules/collections.md#za0111) | Enumerate the ConcurrentDictionary instead of its Keys or Values | Info | Any |
```

- [ ] **Step 9: Commit**

```bash
git add src tests docs/rules/collections.md docs/getting-started.md
git commit -m "feat: add ZA0111 to enumerate ConcurrentDictionary instead of Keys or Values snapshots" -m "Closes #74" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: ZA0212 — use TryParse instead of catching (#77)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/UseTryParseAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0212_UseTryParseTests.cs`
- Modify: `DiagnosticIds.cs`, `AnalyzerReleases.Unshipped.md`, `docs/rules/strings.md`, `docs/getting-started.md`

**Interfaces:**
- Consumes: the Unshipped table.
- Produces: `DiagnosticIds.UseTryParse = "ZA0212"`.

- [ ] **Step 1: Add the ID constant**

After `UseUtf8StringLiteral = "ZA0210";`:

```csharp
    public const string UseTryParse = "ZA0212";
```

ZA0211 goes between them in Task 6.

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0212_UseTryParseTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0212_UseTryParseTests
{
    private static DiagnosticResult Expected(string type) =>
        CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .Diagnostic(DiagnosticIds.UseTryParse)
            .WithLocation(0)
            .WithArguments(type);

    private static string TryCatch(string parse, string catchClause) => $$"""
        using System;

        class Parsable
        {
            public static Parsable Parse(string s) => new();
            public static bool TryParse(string s, out Parsable result) { result = new(); return true; }
        }

        class NoTryParse
        {
            public static NoTryParse Parse(string s) => new();
        }

        class C
        {
            object M(string s)
            {
                try
                {
                    return {{parse}};
                }
                {{catchClause}}
            }
        }
        """;

    [Theory]
    [InlineData("catch (FormatException) { return 0; }")]
    [InlineData("catch (OverflowException) { return 0; }")]
    [InlineData("catch (ArgumentException) { return 0; }")]
    [InlineData("catch (Exception) { return 0; }")]
    [InlineData("catch { return 0; }")]
    public async Task IntParseCaughtAndSwallowed_Reports(string catchClause)
    {
        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyAnalyzerAsync(TryCatch("{|#0:int.Parse(s)|}", catchClause), "net8.0", Expected("int"));
    }

    [Theory]
    [InlineData("{|#0:Enum.Parse<DayOfWeek>(s)|}", "Enum")]
    [InlineData("{|#0:Parsable.Parse(s)|}", "Parsable")]
    public async Task OtherParsableTypes_Report(string parse, string type)
    {
        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyAnalyzerAsync(TryCatch(parse, "catch (Exception) { return 0; }"), "net8.0", Expected(type));
    }

    [Theory]
    [InlineData("int.Parse(s)", "catch (FormatException) { throw; }")]
    [InlineData("int.Parse(s)", "catch (FormatException e) { throw new InvalidOperationException(\"bad\", e); }")]
    [InlineData("int.Parse(s)", "catch (InvalidOperationException) { return 0; }")]
    [InlineData("int.Parse(s)", "catch (FormatException) when (s.Length > 3) { return 0; }")]
    [InlineData("NoTryParse.Parse(s)", "catch (Exception) { return 0; }")]
    public async Task NotSwallowedOrNoTryParse_NoDiagnostic(string parse, string catchClause)
    {
        // Rethrowing, an unrelated catch, a filtered catch, or a type without TryParse.
        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyNoDiagnosticAsync(TryCatch(parse, catchClause), "net8.0");
    }

    [Fact]
    public async Task ParseOutsideTry_NoDiagnostic()
    {
        var source = """
            class C
            {
                int M(string s) => int.Parse(s);
            }
            """;

        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ParseInsideCatchBlock_NoDiagnostic()
    {
        var source = """
            using System;

            class C
            {
                int M(string s, string fallback)
                {
                    try
                    {
                        return s.Length;
                    }
                    catch (Exception)
                    {
                        return int.Parse(fallback);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0212"`
Expected: build error CS0246 for `UseTryParseAnalyzer`.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/UseTryParseAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseTryParseAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseTryParse,
        "Use TryParse instead of catching the exception from Parse",
        "Use '{0}.TryParse' instead of catching the exception from '{0}.Parse'",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // Exceptions Parse throws for bad input, and the base types that also catch them.
    private static readonly string[] ParseExceptionNames =
    [
        "System.Exception",
        "System.SystemException",
        "System.FormatException",
        "System.OverflowException",
        "System.ArgumentException",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            foreach (var name in ParseExceptionNames)
            {
                if (start.Compilation.GetTypeByMetadataName(name) is { } type)
                    builder.Add(type);
            }

            var parseExceptions = builder.ToImmutable();
            start.RegisterOperationAction(context => AnalyzeInvocation(context, parseExceptions), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<INamedTypeSymbol> parseExceptions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var parse = invocation.TargetMethod;
        if (parse.Name != "Parse" || !parse.IsStatic || parse.ReturnsVoid || !HasMatchingTryParse(parse))
            return;

        if (!IsSwallowedByCatch(invocation, parseExceptions))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            parse.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    // TryParse must take Parse's parameters followed by 'out' of Parse's return type.
    private static bool HasMatchingTryParse(IMethodSymbol parse)
    {
        foreach (var candidate in parse.ContainingType.GetMembers("TryParse").OfType<IMethodSymbol>())
        {
            if (!candidate.IsStatic || candidate.Arity != parse.Arity || candidate.Parameters.Length != parse.Parameters.Length + 1)
                continue;

            var tryParse = candidate.Arity > 0 ? candidate.Construct(parse.TypeArguments.ToArray()) : candidate;
            var result = tryParse.Parameters[tryParse.Parameters.Length - 1];
            if (result.RefKind != RefKind.Out || !SymbolEqualityComparer.Default.Equals(result.Type, parse.ReturnType))
                continue;

            var parametersMatch = true;
            for (var i = 0; i < parse.Parameters.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(tryParse.Parameters[i].Type, parse.Parameters[i].Type))
                {
                    parametersMatch = false;
                    break;
                }
            }

            if (parametersMatch)
                return true;
        }

        return false;
    }

    // True when the nearest catch that handles a parse failure swallows it instead of rethrowing.
    private static bool IsSwallowedByCatch(IOperation invocation, ImmutableArray<INamedTypeSymbol> parseExceptions)
    {
        for (var current = invocation; current.Parent is { } parent; current = parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return false;

            if (parent is not ITryOperation tryOperation || tryOperation.Body != current)
                continue;

            foreach (var catchClause in tryOperation.Catches)
            {
                if (catchClause.Filter is not null || !Catches(catchClause.ExceptionType, parseExceptions))
                    continue;

                return !catchClause.Handler.Descendants().OfType<IThrowOperation>().Any();
            }
        }

        return false;
    }

    private static bool Catches(ITypeSymbol exceptionType, ImmutableArray<INamedTypeSymbol> parseExceptions) =>
        exceptionType.SpecialType == SpecialType.System_Object
        || parseExceptions.Contains(exceptionType as INamedTypeSymbol, SymbolEqualityComparer.Default);
}
```

- [ ] **Step 5: Add the release-tracking row**

```
ZA0212  | Performance.Strings     | Info     | UseTryParseAnalyzer
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0212"`
Expected: PASS, 14 tests.

- [ ] **Step 7: Document the rule**

Append to `docs/rules/strings.md`:

````markdown

---

## ZA0212 — Use TryParse instead of catching the exception from Parse {#za0212}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

When `Parse` fails it allocates an exception and captures a stack trace, which costs far more than the parse itself. Code that catches that exception to fall back to a default pays this on every bad input. `TryParse` reports failure through its return value and allocates nothing. The rule reports a `Parse` call inside a `try` whose matching `catch` swallows the exception, for any type with a `TryParse` that takes the same parameters plus an `out` result. That covers the numeric types, `Guid`, `DateTime`, `Enum.Parse<T>` and your own `IParsable<T>` types.

### Before

```csharp
// ❌ every malformed value allocates and throws an exception
int port;
try
{
    port = int.Parse(value);
}
catch (FormatException)
{
    port = DefaultPort;
}
```

### After

```csharp
// ✓ no exception on bad input
if (!int.TryParse(value, out var port))
    port = DefaultPort;
```

### Suppression

```csharp
#pragma warning disable ZA0212
// or in .editorconfig: dotnet_diagnostic.ZA0212.severity = none
```
````

In `docs/getting-started.md`, add after the ZA0210 row:

```markdown
| [ZA0212](rules/strings.md#za0212) | Use TryParse instead of catching the exception from Parse | Info | Any |
```

- [ ] **Step 8: Commit**

```bash
git add src tests docs/rules/strings.md docs/getting-started.md
git commit -m "feat: add ZA0212 to prefer TryParse over catching Parse exceptions" -m "Closes #77" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: ZA0211 — span-based split (#76)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/UseSpanSplitAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0211_UseSpanSplitTests.cs`
- Modify: `DiagnosticIds.cs`, `AnalyzerReleases.Unshipped.md`, `docs/rules/strings.md`, `docs/getting-started.md`

**Interfaces:**
- Consumes: the Unshipped table.
- Produces: `DiagnosticIds.UseSpanSplit = "ZA0211"`.

- [ ] **Step 1: Add the ID constant**

Between `UseUtf8StringLiteral` and `UseTryParse`:

```csharp
    public const string UseSpanSplit = "ZA0211";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0211_UseSpanSplitTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0211_UseSpanSplitTests
{
    private static string Loop(string collection) => $$"""
        using System;

        class C
        {
            int M(string csv)
            {
                var count = 0;
                foreach (var part in {{collection}})
                    count += part.Length;
                return count;
            }
        }
        """;

    private static DiagnosticResult Expected() =>
        CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .Diagnostic(DiagnosticIds.UseSpanSplit)
            .WithLocation(0)
            .WithArguments("csv");

    [Theory]
    [InlineData("{|#0:csv.Split(',')|}")]
    [InlineData("{|#0:csv.Split(\", \")|}")]
    [InlineData("{|#0:csv.Split(',', StringSplitOptions.None)|}")]
    public async Task ForeachOverSplit_OnNet9_Reports(string collection)
    {
        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyAnalyzerAsync(Loop(collection), "net9.0", ReferenceAssemblies.Net.Net90, Expected());
    }

    [Theory]
    [InlineData("csv.Split(',', StringSplitOptions.RemoveEmptyEntries)")]
    [InlineData("csv.Split(',', ';')")]
    [InlineData("csv.Split(\"\")")]
    public async Task UnsupportedSplitForms_NoDiagnostic(string collection)
    {
        // The span enumerator has no StringSplitOptions and splits on one separator.
        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyNoDiagnosticAsync(Loop(collection), "net9.0", ReferenceAssemblies.Net.Net90);
    }

    [Fact]
    public async Task ForeachOverSplit_OnNet8_NoDiagnostic()
    {
        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyNoDiagnosticAsync(Loop("csv.Split(',')"), "net8.0");
    }

    [Fact]
    public async Task SplitResultKept_NoDiagnostic()
    {
        var source = """
            class C
            {
                int M(string csv)
                {
                    var parts = csv.Split(',');
                    return parts.Length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net9.0", ReferenceAssemblies.Net.Net90);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0211"`
Expected: build error CS0246 for `UseSpanSplitAnalyzer`.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/UseSpanSplitAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseSpanSplitAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseSpanSplit,
        "Use the span-based Split",
        "Enumerate '{0}.AsSpan().Split(...)' to split without allocating the parts",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // .NET 9 adds MemoryExtensions.Split<T> returning this enumerator.
            if (start.Compilation.GetTypeByMetadataName("System.MemoryExtensions+SpanSplitEnumerator`1") is null)
                return;

            start.RegisterOperationAction(AnalyzeForEach, OperationKind.Loop);
        });
    }

    private static void AnalyzeForEach(OperationAnalysisContext context)
    {
        if (context.Operation is not IForEachLoopOperation loop)
            return;

        var collection = loop.Collection;
        while (collection is IConversionOperation { IsImplicit: true } conversion)
            collection = conversion.Operand;

        if (collection is not IInvocationOperation { TargetMethod: { Name: "Split" } method, Instance: { } receiver } split
            || method.ContainingType.SpecialType != SpecialType.System_String
            || method.Parameters.Length != 2
            || method.Parameters[1].Type.Name != "StringSplitOptions")
        {
            return;
        }

        var separator = split.Arguments.First(a => a.Parameter?.Ordinal == 0);
        var separatorIsSupported = method.Parameters[0].Type.SpecialType switch
        {
            SpecialType.System_Char => true,
            SpecialType.System_String => separator.Value.ConstantValue is { HasValue: true, Value: string { Length: > 0 } },
            _ => false,
        };

        var options = split.Arguments.First(a => a.Parameter?.Ordinal == 1);
        var noOptions = options.ArgumentKind == ArgumentKind.DefaultValue
            || options.Value.ConstantValue is { HasValue: true, Value: 0 };

        if (separatorIsSupported && noOptions)
            context.ReportDiagnostic(Diagnostic.Create(Rule, split.Syntax.GetLocation(), receiver.Syntax.ToString()));
    }
}
```

- [ ] **Step 5: Add the release-tracking row**

```
ZA0211  | Performance.Strings     | Info     | UseSpanSplitAnalyzer
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0211"`
Expected: PASS, 8 tests.

- [ ] **Step 7: Document the rule**

Append to `docs/rules/strings.md`:

````markdown

---

## ZA0211 — Use the span-based Split {#za0211}

> **Severity**: Info | **Min TFM**: net9.0 | **Code fix**: No

### Why

`string.Split` allocates the result array and a new string for every part, even when the loop only looks at each part once. Since .NET 9, `MemoryExtensions.Split` on a `ReadOnlySpan<char>` enumerates the parts as `Range` values over the original text, with no allocation. The rule reports a `foreach` directly over `string.Split` with a single `char` or non-empty constant string separator and no `StringSplitOptions`, because the span enumerator supports neither multiple separators nor options. There is no code fix: the loop variable changes from `string` to `Range`, so the body has to change.

### Before

```csharp
// ❌ allocates the array and one string per field
foreach (var field in line.Split(','))
    total += int.Parse(field);
```

### After

```csharp
// ✓ no allocation
var span = line.AsSpan();
foreach (var range in span.Split(','))
    total += int.Parse(span[range]);
```

### Suppression

```csharp
#pragma warning disable ZA0211
// or in .editorconfig: dotnet_diagnostic.ZA0211.severity = none
```
````

In `docs/getting-started.md`, add after the ZA0210 row:

```markdown
| [ZA0211](rules/strings.md#za0211) | Use the span-based Split | Info | net9.0 |
```

- [ ] **Step 8: Commit**

```bash
git add src tests docs/rules/strings.md docs/getting-started.md
git commit -m "feat: add ZA0211 to suggest the span-based Split on .NET 9" -m "Closes #76" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: ZA0110 — declare params as ReadOnlySpan (#73)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/PreferParamsSpanAnalyzer.cs`
- Create: `src/ZeroAlloc.Analyzers.CodeFixes/PreferParamsSpanCodeFixProvider.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0110_PreferParamsSpanTests.cs`
- Modify: `DiagnosticIds.cs`, `AnalyzerReleases.Unshipped.md`, `docs/rules/collections.md`, `docs/getting-started.md`

**Interfaces:**
- Consumes:
  - `ReadOnlySpanUse.IsCompatible(IOperation, INamedTypeSymbol)` and `UsingDirectives.EnsureSystem(SyntaxNode)` from Task 3;
  - `CSharpAnalyzerVerifier<T>.VerifyAnalyzerAsync(string, LanguageVersion, params DiagnosticResult[])` from Task 2.
- Produces: `DiagnosticIds.PreferParamsSpan = "ZA0110"`. Diagnostics carry the property `"Exposed"`: `"true"` for methods visible outside the assembly, which get no code fix.

- [ ] **Step 1: Add the ID constant**

After `AvoidZeroLengthArrayAllocation = "ZA0109";`:

```csharp
    public const string PreferParamsSpan = "ZA0110";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0110_PreferParamsSpanTests.cs`:

```csharp
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0110_PreferParamsSpanTests
{
    private const string ExposedSuffix = "; it is visible outside the assembly, so add an overload instead of changing it";

    private static DiagnosticResult Expected(string method, string suffix = "") =>
        CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .Diagnostic(DiagnosticIds.PreferParamsSpan)
            .WithLocation(0)
            .WithArguments("values", method, "int", suffix);

    [Fact]
    public async Task PrivateMethodReadingTheArray_ReportsAndFixes()
    {
        var source = """
            using System;

            class C
            {
                static int Sum(params int[] {|#0:values|})
                {
                    var total = values.Length + values[0];
                    foreach (var value in values)
                        total += value;
                    return total;
                }
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                static int Sum(params ReadOnlySpan<int> values)
                {
                    var total = values.Length + values[0];
                    foreach (var value in values)
                        total += value;
                    return total;
                }
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Sum"));
    }

    [Fact]
    public async Task PassedOnAsSpan_Reports()
    {
        var source = """
            using System;

            class C
            {
                static int Inner(ReadOnlySpan<int> span) => span.Length;

                static int Outer(params int[] {|#0:values|}) => Inner(values);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("Outer"));
    }

    [Fact]
    public async Task PublicMethod_ReportsOverloadAdvice()
    {
        var source = """
            public class C
            {
                public static int Sum(params int[] {|#0:values|}) => values.Length;
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("Sum", ExposedSuffix));
    }

    [Fact]
    public async Task CSharp12_NoDiagnostic()
    {
        // params collections arrived in C# 13.
        var source = """
            class C
            {
                static int Sum(params int[] values) => values.Length;
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, LanguageVersion.CSharp12);
    }

    [Theory]
    [InlineData("static int[] Keep(params int[] values) => values;")]
    [InlineData("static int[] _kept = new int[0]; static void Keep(params int[] values) => _kept = values;")]
    [InlineData("static int Inner(int[] array) => array.Length; static int Keep(params int[] values) => Inner(values);")]
    [InlineData("static void Keep(params int[] values) => values[0] = 1;")]
    [InlineData("static Func<int> Keep(params int[] values) => () => values.Length;")]
    [InlineData("static int Keep(params int[] values) => values.Length; static Func<int[], int> Group = Keep;")]
    [InlineData("static IEnumerable<int> Keep(params int[] values) { foreach (var v in values) yield return v; }")]
    [InlineData("static async Task<int> Keep(params int[] values) { await Task.Yield(); return values.Length; }")]
    [InlineData("public virtual int Keep(params int[] values) => values.Length;")]
    public async Task ArrayIsNeededOrSignatureIsFixed_NoDiagnostic(string member)
    {
        // Escapes, writes, captures, method groups, iterators, async, and virtual methods.
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            class C
            {
                {{member}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task OverrideAndInterfaceImplementation_NoDiagnostic()
    {
        var source = """
            interface ISummer
            {
                int Sum(params int[] values);
            }

            abstract class Base
            {
                public abstract int Count(params int[] values);
            }

            class C : Base, ISummer
            {
                public int Sum(params int[] values) => values.Length;

                public override int Count(params int[] values) => values.Length;
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0110"`
Expected: build errors CS0246 for the analyzer and code fix types.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/PreferParamsSpanAnalyzer.cs`:

```csharp
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreferParamsSpanAnalyzer : DiagnosticAnalyzer
{
    public const string ExposedProperty = "Exposed";

    private const string ExposedSuffix = "; it is visible outside the assembly, so add an overload instead of changing it";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.PreferParamsSpan,
        "Declare params as ReadOnlySpan<T>",
        "params parameter '{0}' of '{1}' can be 'params ReadOnlySpan<{2}>' so callers do not allocate{3}",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation is not CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp13 })
                return;

            var readOnlySpan = start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            if (readOnlySpan is null)
                return;

            var candidates = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
            var methodGroups = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

            // A method group converted to a delegate fixes the parameter type.
            start.RegisterOperationAction(context =>
            {
                var reference = (IMethodReferenceOperation)context.Operation;
                methodGroups[reference.Method.OriginalDefinition] = true;
            }, OperationKind.MethodReference);

            start.RegisterOperationBlockAction(context =>
            {
                if (context.OwningSymbol is IMethodSymbol method
                    && IsCandidate(method)
                    && UsesAreSpanCompatible(context.OperationBlocks, method.Parameters[method.Parameters.Length - 1], readOnlySpan))
                {
                    candidates[method] = true;
                }
            });

            start.RegisterCompilationEndAction(context =>
            {
                foreach (var method in candidates.Keys)
                {
                    if (methodGroups.ContainsKey(method))
                        continue;

                    var parameter = method.Parameters[method.Parameters.Length - 1];
                    var exposed = IsVisibleOutsideAssembly(method);
                    var properties = ImmutableDictionary<string, string?>.Empty
                        .Add(ExposedProperty, exposed ? "true" : "false");

                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        parameter.Locations[0],
                        properties,
                        parameter.Name,
                        method.Name,
                        ((IArrayTypeSymbol)parameter.Type).ElementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        exposed ? ExposedSuffix : string.Empty));
                }
            });
        });
    }

    private static bool IsCandidate(IMethodSymbol method)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)
            || method.Parameters.IsEmpty
            || method.Parameters[method.Parameters.Length - 1] is not { IsParams: true, Type: IArrayTypeSymbol { Rank: 1 } })
        {
            return false;
        }

        // The signature is fixed by another declaration, or a span parameter is not allowed.
        if (method.IsOverride || method.IsVirtual || method.IsAbstract || method.IsExtern
            || method.IsAsync || method.IsIterator
            || method.PartialDefinitionPart is not null || method.PartialImplementationPart is not null
            || !method.ExplicitInterfaceImplementations.IsEmpty)
        {
            return false;
        }

        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method))
                    return false;
            }
        }

        return true;
    }

    private static bool UsesAreSpanCompatible(ImmutableArray<IOperation> blocks, IParameterSymbol parameter, INamedTypeSymbol readOnlySpan)
    {
        foreach (var block in blocks)
        {
            foreach (var reference in block.Descendants().OfType<IParameterReferenceOperation>())
            {
                if (!SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter))
                    continue;

                // A span cannot be captured by a lambda or local function.
                for (var current = reference.Parent; current is not null; current = current.Parent)
                {
                    if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                        return false;
                }

                if (!ReadOnlySpanUse.IsCompatible(reference, readOnlySpan))
                    return false;
            }
        }

        return true;
    }

    private static bool IsVisibleOutsideAssembly(ISymbol symbol)
    {
        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                return false;
        }

        return true;
    }
}
```

- [ ] **Step 5: Write the code fix**

Create `src/ZeroAlloc.Analyzers.CodeFixes/PreferParamsSpanCodeFixProvider.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class PreferParamsSpanCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.PreferParamsSpan];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];

        // Changing a signature other assemblies call is a binary breaking change.
        if (diagnostic.Properties.TryGetValue(PreferParamsSpanAnalyzer.ExposedProperty, out var exposed) && exposed == "true")
            return;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root?.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ParameterSyntax>() is not
            { Type: ArrayTypeSyntax arrayType } parameter)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Declare as params ReadOnlySpan<T>",
                ct => ReplaceAsync(context.Document, parameter, arrayType, ct),
                equivalenceKey: DiagnosticIds.PreferParamsSpan),
            diagnostic);
    }

    private static async Task<Document> ReplaceAsync(Document document, ParameterSyntax parameter, ArrayTypeSyntax arrayType, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var spanType = SyntaxFactory.ParseTypeName($"ReadOnlySpan<{arrayType.ElementType}>").WithTriviaFrom(arrayType);
        var newRoot = root.ReplaceNode(parameter, parameter.WithType(spanType));
        return document.WithSyntaxRoot(UsingDirectives.EnsureSystem(newRoot));
    }
}
```

`PreferParamsSpanAnalyzer.ExposedProperty` is public on the analyzer, and the code fix project already references the analyzer project, so the constant is shared rather than duplicated.

- [ ] **Step 6: Add the release-tracking row**

```
ZA0110  | Performance.Collections | Info     | PreferParamsSpanAnalyzer
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA0110"`
Expected: PASS, 14 tests.

- [ ] **Step 8: Document the rule**

Append to `docs/rules/collections.md`:

````markdown

---

## ZA0110 — Declare params as ReadOnlySpan\<T\> {#za0110}

> **Severity**: Info | **Min TFM**: Any (C# 13) | **Code fix**: Yes, for methods not visible outside the assembly

### Why

Every call to a `params T[]` method that passes individual arguments allocates a new array. Since C# 13, a `params ReadOnlySpan<T>` parameter gets its arguments from an inline array on the stack on .NET 8 and later, so calls allocate nothing. This is the declaration side of [ZA0602](linq.md#za0602).

The rule reports a `params T[]` parameter whose method only reads it: indexing, `Length`, `foreach`, or passing it on as a `ReadOnlySpan<T>`. A method that stores, returns or captures the array, writes its elements, or is used as a method group keeps the array. So do overrides, interface implementations, virtual, abstract and partial methods, async methods and iterators.

For a method other assemblies can call, changing the parameter type is a binary breaking change, so the rule suggests adding a `params ReadOnlySpan<T>` overload instead and offers no code fix. Because it has to see every use of the method, the rule reports when the whole project is analyzed, on build or with full-solution analysis, not while you type.

### Before

```csharp
// ❌ every call allocates an int[]
private static int Max(params int[] values)
{
    var max = int.MinValue;
    foreach (var value in values)
        max = Math.Max(max, value);
    return max;
}
```

### After

```csharp
// ✓ calls pass an inline array on the stack
private static int Max(params ReadOnlySpan<int> values)
{
    var max = int.MinValue;
    foreach (var value in values)
        max = Math.Max(max, value);
    return max;
}
```

### Suppression

```csharp
#pragma warning disable ZA0110
// or in .editorconfig: dotnet_diagnostic.ZA0110.severity = none
```
````

In `docs/getting-started.md`, add after the ZA0109 row, before ZA0111:

```markdown
| [ZA0110](rules/collections.md#za0110) | Declare params as ReadOnlySpan\<T\> | Info | Any (C# 13) |
```

- [ ] **Step 9: Commit**

```bash
git add src tests docs/rules/collections.md docs/getting-started.md
git commit -m "feat: add ZA0110 to suggest declaring params as ReadOnlySpan" -m "Closes #73" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Rule counts, spec refinements, and the PR

**Files:**
- Modify: `README.md`, `docs/getting-started.md`, `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`

- [ ] **Step 1: Update rule counts**

Seven rules take the total from 50 to 57: Collections 9 to 11, Strings 8 to 11, Memory 2 to 4.

- `README.md` line 9 and line 59: replace `50 rules` with `57 rules`.
- `README.md` Memory row in the documentation table: `stackalloc for small buffers, ArrayPool for large temporary arrays, returning rented arrays, constant tables as spans`.
- `README.md` performance table "Rules active" column:
  - `netstandard2.0`: `40 of 57`. The four rules gated on neither C# 11+ nor .NET 9 are added: ZA0111, ZA0212, ZA0303 and ZA0304.
  - `net8.0`: `55 of 57`. ZA0211 needs .NET 9, and ZA0110 needs C# 13, while net8.0 defaults to C# 12.
  - multi-TFM: `55 / 40 per TFM`.
  - data-flow rules disabled: `53 of 57`.
  - Leave the timing columns unchanged; they are typical figures, not measurements of this change.
- `docs/getting-started.md` lines 13, 124 and 150: replace `50 rules` with `57 rules`. In the mermaid graph, change `ZA01xx (9 rules)` to `ZA01xx (11 rules)`, `ZA02xx (8 rules)` to `ZA02xx (11 rules)` and `ZA03xx (2 rules)` to `ZA03xx (4 rules)`.

Run: `grep -rn "50 rules\|of 50\|/ 36" README.md docs/getting-started.md`
Expected: no output.

- [ ] **Step 2: Record the refinements in the design doc**

In `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`:

1. Under **ZA0303 → Detection**, add: `Passing the array to a method of System.MemoryExtensions, System.Array, System.Buffer or System.IO.Stream, including Stream subclasses, is not an escape: those APIs read or write the array without taking ownership.`
2. Under **ZA0304 → Detection** and **ZA0110 → Detection**, add: `Reported from a compilation-end action, because the check needs every use of the symbol in the compilation.` For ZA0110 also add: `Methods used as a method group anywhere in the compilation are not reported, nor are virtual, abstract or partial methods, async methods and iterators.`
3. Under **ZA0111 → Code fix**, replace the text with: `For foreach, deconstruct the pair: 'foreach (var key in map.Keys)' becomes 'foreach (var (key, _) in map)', leaving the loop body unchanged. Offered only when KeyValuePair<TKey, TValue>.Deconstruct exists and the loop variable is 'var' or the element type.`

- [ ] **Step 3: Run the full suite and build**

Run: `dotnet build -c Release && dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release`
Expected: build succeeds with 0 warnings, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add README.md docs
git commit -m "docs: update rule counts and record batch 3 design refinements" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin feat/batch-3-pr1
gh pr create --base main --title "feat: add batch 3 allocation rules ZA0110 to ZA0304" --body-file pr-body.md
```

The PR body must contain this block, so the squash merge credits every rule:

```
BEGIN_COMMIT_OVERRIDE
feat: add ZA0110 to suggest declaring params as ReadOnlySpan
feat: add ZA0111 to enumerate ConcurrentDictionary instead of Keys or Values snapshots
feat: add ZA0210 to replace constant UTF-8 encoding with u8 literals
feat: add ZA0211 to suggest the span-based Split on .NET 9
feat: add ZA0212 to prefer TryParse over catching Parse exceptions
feat: add ZA0303 to report rented arrays never returned to the pool
feat: add ZA0304 to turn constant lookup tables into ReadOnlySpan properties
END_COMMIT_OVERRIDE
```

Also include `Closes #73, #74, #75, #76, #77, #78, #79`, a short summary per rule, and the `🤖 Generated with [Claude Code](https://claude.com/claude-code)` line.

- [ ] **Step 6: Wait for CI**

Run: `gh pr checks --watch`
Expected: `build` and `lint-commits` pass.
