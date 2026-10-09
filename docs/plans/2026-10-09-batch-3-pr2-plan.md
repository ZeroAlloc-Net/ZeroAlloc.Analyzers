# Batch 3 PR 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship ZA1503 (struct hash key without `IEquatable<T>`) and ZA1402 (state-passing overload instead of a capturing lambda, with a code fix).

**Architecture:** Each rule is one `DiagnosticAnalyzer` in `src/ZeroAlloc.Analyzers/Analyzers/` built on `IOperation`, with every API gated on its symbol existing in the compilation. ZA1402 does all of its semantic checks in the analyzer and passes the fix decision to its code fix through diagnostic properties, so the code fix only rewrites syntax.

**Tech Stack:** C# with `LangVersion` latest, Roslyn 5.9, xUnit with `Microsoft.CodeAnalysis.CSharp.Analyzer.Testing` 1.1.4.

**Spec:** `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`, sections ZA1402 and ZA1503. Issues #80 and #81.

## Global Constraints

- The analyzer project targets `netstandard2.0` with `TreatWarningsAsErrors`, `Nullable`, `ImplicitUsings` and `EnforceExtendedAnalyzerRules` on. Do not add explicit `using System.Linq;`, `using System.Collections.Generic;` or `using System.Threading.Tasks;`.
- `netstandard2.0` has no `IsExternalInit`, so do not declare `record` types in the analyzer project. Use a `sealed class` instead.
- Every new descriptor needs a `DiagnosticIds` constant and a row appended to the existing `### New Rules` table in `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md`.
- Analyze through `IOperation`. Gate on BCL APIs by checking that their symbol exists, not by parsing the TFM.
- Both rules are `Info`.
- Code fixes find their node with `root.FindNode(span, getInnermostNodeForTie: true)`. PR 1 showed that without the flag, `FindNode` returns the enclosing `Argument`.
- Keep each file's existing line endings, and do not touch the line endings of lines you don't edit.
- Make one `feat:` commit per rule. Body lines must be 100 characters or fewer, because commitlint enforces `body-max-line-length` 100 in CI. Bodies must not contain nested parentheses.
- Each commit ends with a `Co-Authored-By:` trailer naming the model that wrote it.
- Build and test from the repo root: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release`

## File map

| File | Responsibility | Task |
|---|---|---|
| `src/ZeroAlloc.Analyzers/DiagnosticIds.cs` | ID constants | 1, 2 |
| `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md` | Release tracking rows | 1, 2 |
| `src/ZeroAlloc.Analyzers/Analyzers/ImplementEquatableOnStructKeyAnalyzer.cs` | ZA1503 | 1 |
| `src/ZeroAlloc.Analyzers/Analyzers/UseStatePassingOverloadAnalyzer.cs` | ZA1402 | 2 |
| `src/ZeroAlloc.Analyzers.CodeFixes/UseStatePassingOverloadCodeFixProvider.cs` | ZA1402 fix | 2 |
| `docs/rules/value-types.md`, `docs/rules/delegates.md` | Rule sections | 1, 2 |
| `docs/getting-started.md`, `README.md`, `docs/performance.md` | Rows, counts | 1, 2, 3 |
| `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md` | Record refinements | 3 |

## Refinements to the spec made by this plan

Task 3 writes these into the design doc:

1. **ZA1402 code fix scope.** The fix is offered only for APIs whose state-passing overload takes a *typed* state. `CancellationToken.Register`, `UnsafeRegister` and `TaskFactory.StartNew` take `object? state`, so a fix would need casts in the lambda body. `ConcurrentDictionary.AddOrUpdate` has two lambdas sharing one argument. `string.Create` already has a state that would have to become a tuple. Those APIs get the diagnostic only.
2. **The ZA1402 fix runs only when it preserves meaning.** It requires all of the following:
   - exactly one captured local or parameter, with no `this`, no `ref` local and no `ref`/`out` parameter;
   - the captured variable is never written anywhere in its declaring body, because a closure sees later writes while state is a snapshot;
   - the lambda is implicitly typed;
   - for `QueueUserWorkItem`, the lambda does not use its own parameter.

   For `QueueUserWorkItem`, the fix passes `preferLocal: false`, which matches the `WaitCallback` overload's global-queue behaviour.
3. **ZA1402 overlaps ZA0502.** Inside a loop, a capturing lambda passed to one of these APIs is reported by both rules. ZA0502 says where the allocation is; ZA1402 gives the API-specific fix. The rule docs say so.
4. **ZA1503 checks the arguments actually passed.** A call is reported unless an argument supplies a non-null `IEqualityComparer<T>`. Checking parameters is not enough, because `ToFrozenSet` and `ToFrozenDictionary` take an optional comparer that defaults to null. Collection expressions such as `HashSet<Key> set = [a, b];` are not covered.

---

### Task 1: ZA1503 — struct hash key without IEquatable (#81)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/ImplementEquatableOnStructKeyAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA1503_ImplementEquatableOnStructKeyTests.cs`
- Modify: `src/ZeroAlloc.Analyzers/DiagnosticIds.cs`, `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md`, `docs/rules/value-types.md`, `docs/getting-started.md`

**Interfaces:**
- Produces: `DiagnosticIds.ImplementEquatableOnStructKey = "ZA1503"`.

- [ ] **Step 1: Add the ID constant**

In `DiagnosticIds.cs`, after `public const string AvoidFinalizers = "ZA1502";`:

```csharp
    public const string ImplementEquatableOnStructKey = "ZA1503";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA1503_ImplementEquatableOnStructKeyTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1503_ImplementEquatableOnStructKeyTests
{
    private static DiagnosticResult Expected(string type = "Point") =>
        CSharpAnalyzerVerifier<ImplementEquatableOnStructKeyAnalyzer>
            .Diagnostic(DiagnosticIds.ImplementEquatableOnStructKey)
            .WithLocation(0)
            .WithArguments(type);

    private static string WithKeys(string statement) => $$"""
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Frozen;
        using System.Collections.Generic;
        using System.Linq;

        struct Point
        {
            public int X;
            public int Y;
        }

        struct EquatablePoint : IEquatable<EquatablePoint>
        {
            public int X;
            public bool Equals(EquatablePoint other) => X == other.X;
            public override int GetHashCode() => X;
        }

        record struct RecordPoint(int X);

        enum Color { Red }

        class Box { }

        sealed class PointComparer : IEqualityComparer<Point>
        {
            public static readonly PointComparer Instance = new();
            public bool Equals(Point a, Point b) => a.X == b.X && a.Y == b.Y;
            public int GetHashCode(Point p) => p.X;
        }

        class C
        {
            object M(Point[] points)
            {
                {{statement}}
            }
        }
        """;

    [Theory]
    [InlineData("return {|#0:new Dictionary<Point, int>()|};")]
    [InlineData("return {|#0:new HashSet<Point>()|};")]
    [InlineData("return {|#0:new ConcurrentDictionary<Point, int>()|};")]
    [InlineData("return {|#0:new Dictionary<Point, int>(new Dictionary<Point, int>(PointComparer.Instance))|};")]
    [InlineData("return {|#0:points.ToDictionary(p => p, p => 0)|};")]
    [InlineData("return {|#0:points.ToHashSet()|};")]
    [InlineData("return {|#0:points.ToFrozenSet()|};")]
    [InlineData("return {|#0:points.Select(p => new KeyValuePair<Point, int>(p, 0)).ToFrozenDictionary()|};")]
    public async Task StructKeyWithoutEquatable_Reports(string statement)
    {
        await CSharpAnalyzerVerifier<ImplementEquatableOnStructKeyAnalyzer>
            .VerifyAnalyzerAsync(WithKeys(statement), "net8.0", Expected());
    }

    [Theory]
    [InlineData("return new Dictionary<EquatablePoint, int>();")]
    [InlineData("return new HashSet<RecordPoint>();")]
    [InlineData("return new Dictionary<Color, int>();")]
    [InlineData("return new Dictionary<Box, int>();")]
    [InlineData("return new Dictionary<Point, int>(PointComparer.Instance);")]
    [InlineData("return new HashSet<Point>(points, PointComparer.Instance);")]
    [InlineData("return points.ToDictionary(p => p, p => 0, PointComparer.Instance);")]
    [InlineData("return points.ToHashSet(PointComparer.Instance);")]
    [InlineData("return points.ToFrozenSet(PointComparer.Instance);")]
    [InlineData("return new Dictionary<KeyValuePair<int, int>, int>();")]
    public async Task KeyIsFineOrComparerPassed_NoDiagnostic(string statement)
    {
        // Equatable, record and enum keys, reference keys, an explicit comparer, and a struct
        // from another assembly that the user cannot change.
        await CSharpAnalyzerVerifier<ImplementEquatableOnStructKeyAnalyzer>
            .VerifyNoDiagnosticAsync(WithKeys(statement), "net8.0");
    }
}
```

The fourth positive case reports only the outer `Dictionary`. Its argument is a dictionary that already has a comparer, but the copy constructor does not reuse it, and that call takes no `IEqualityComparer<T>` parameter.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1503"`
Expected: build error CS0246, `ImplementEquatableOnStructKeyAnalyzer` not found.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/ImplementEquatableOnStructKeyAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ImplementEquatableOnStructKeyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ImplementEquatableOnStructKey,
        "Implement IEquatable<T> on structs used as hash keys",
        "Struct '{0}' is used as a hash key but does not implement IEquatable<{0}>, so every lookup boxes it",
        DiagnosticCategories.ValueTypes,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // Hashed collections whose first type argument is the key.
    private static readonly string[] CollectionTypeNames =
    [
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Concurrent.ConcurrentDictionary`2",
    ];

    // Static factories that build a hashed collection from a sequence.
    private static readonly string[] FactoryTypeNames =
    [
        "System.Linq.Enumerable",
        "System.Collections.Frozen.FrozenDictionary",
        "System.Collections.Frozen.FrozenSet",
    ];

    private static readonly string[] FactoryMethodNames = ["ToDictionary", "ToHashSet", "ToFrozenDictionary", "ToFrozenSet"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var equatable = start.Compilation.GetTypeByMetadataName("System.IEquatable`1");
            var comparer = start.Compilation.GetTypeByMetadataName("System.Collections.Generic.IEqualityComparer`1");
            if (equatable is null || comparer is null)
                return;

            var known = new KnownTypes(
                start.Compilation.Assembly,
                equatable,
                comparer,
                Resolve(start.Compilation, CollectionTypeNames),
                Resolve(start.Compilation, FactoryTypeNames));

            start.RegisterOperationAction(context => AnalyzeObjectCreation(context, known), OperationKind.ObjectCreation);
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

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, KnownTypes known)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is not INamedTypeSymbol { IsGenericType: true } type
            || !known.Collections.Contains(type.OriginalDefinition, SymbolEqualityComparer.Default)
            || known.PassesComparer(creation.Arguments))
        {
            return;
        }

        Report(context, known, type.TypeArguments[0], creation.Syntax);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes known)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!method.IsGenericMethod
            || Array.IndexOf(FactoryMethodNames, method.Name) < 0
            || !known.Factories.Contains(method.ContainingType.OriginalDefinition, SymbolEqualityComparer.Default)
            || known.PassesComparer(invocation.Arguments))
        {
            return;
        }

        // ToDictionary and ToFrozenDictionary name their key TKey; ToHashSet and ToFrozenSet key on their only type argument.
        var keyIndex = 0;
        for (var i = 0; i < method.TypeParameters.Length; i++)
        {
            if (method.TypeParameters[i].Name == "TKey")
                keyIndex = i;
        }

        Report(context, known, method.TypeArguments[keyIndex], invocation.Syntax);
    }

    private static void Report(OperationAnalysisContext context, KnownTypes known, ITypeSymbol key, SyntaxNode syntax)
    {
        if (key is not INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRecord: false } structType
            || !SymbolEqualityComparer.Default.Equals(structType.ContainingAssembly, known.Assembly)
            || known.ImplementsEquatable(structType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            syntax.GetLocation(),
            structType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private sealed class KnownTypes(
        IAssemblySymbol assembly,
        INamedTypeSymbol equatable,
        INamedTypeSymbol comparer,
        ImmutableArray<INamedTypeSymbol> collections,
        ImmutableArray<INamedTypeSymbol> factories)
    {
        public IAssemblySymbol Assembly { get; } = assembly;

        public ImmutableArray<INamedTypeSymbol> Collections { get; } = collections;

        public ImmutableArray<INamedTypeSymbol> Factories { get; } = factories;

        // ToFrozenSet and ToFrozenDictionary take an optional comparer, so the parameter alone proves
        // nothing: a comparer counts only when an argument actually supplies a non-null one.
        public bool PassesComparer(ImmutableArray<IArgumentOperation> arguments) =>
            arguments.Any(a => a.ArgumentKind != ArgumentKind.DefaultValue
                && a.Parameter?.Type is INamedTypeSymbol named
                && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, comparer)
                && a.Value.ConstantValue is not { HasValue: true, Value: null });

        public bool ImplementsEquatable(INamedTypeSymbol type) =>
            type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, equatable)
                && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type));
    }
}
```

- [ ] **Step 5: Add the release-tracking row**

Append to the `### New Rules` table in `AnalyzerReleases.Unshipped.md`:

```
ZA1503  | Performance.ValueTypes  | Info     | ImplementEquatableOnStructKeyAnalyzer
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1503"`
Expected: PASS, 18 tests.

- [ ] **Step 7: Document the rule**

In `docs/rules/value-types.md`, insert this section after the ZA1501 section and before the `---` that precedes ZA1502, so the sections stay in numeric order. Change the intro paragraph's last sentence from "The ZA15xx rules catch both patterns." to "The ZA15xx rules catch these patterns."

````markdown
## ZA1503 — Implement IEquatable\<T\> on structs used as hash keys {#za1503}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: No

### Why

`Dictionary`, `HashSet` and `ConcurrentDictionary` compare keys through `EqualityComparer<T>.Default`. For a struct that does not implement `IEquatable<T>`, that comparer falls back to `Equals(object)`, which boxes the key on every lookup, insert and remove. Implementing `IEquatable<T>` gives the comparer a strongly typed `Equals` that does not allocate. [ZA1501](#za1501) covers the `GetHashCode` half of the same problem.

The rule reports a hashed collection, or a `ToDictionary`, `ToHashSet`, `ToFrozenDictionary` or `ToFrozenSet` call, keyed on a struct from your own code that lacks `IEquatable<T>`, when no `IEqualityComparer<T>` is passed. Record structs already implement it, and enums and primitives have a non-boxing comparer.

### Before

```csharp
// ❌ every lookup boxes the key to call Equals(object)
public struct CellId
{
    public int Row;
    public int Column;
    public override int GetHashCode() => HashCode.Combine(Row, Column);
}

var cells = new Dictionary<CellId, Cell>();
```

### After

```csharp
// ✓ the default comparer calls Equals(CellId) directly
public readonly struct CellId : IEquatable<CellId>
{
    public int Row { get; init; }
    public int Column { get; init; }
    public bool Equals(CellId other) => Row == other.Row && Column == other.Column;
    public override bool Equals(object? obj) => obj is CellId other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Row, Column);
}

var cells = new Dictionary<CellId, Cell>();
```

### Suppression

```csharp
#pragma warning disable ZA1503
// or in .editorconfig: dotnet_diagnostic.ZA1503.severity = none
```

---

````

In `docs/getting-started.md`, add after the ZA1502 row in the Value Types table:

```markdown
| [ZA1503](rules/value-types.md#za1503) | Implement IEquatable\<T\> on structs used as hash keys | Info | Any |
```

- [ ] **Step 8: Commit**

```bash
git add src tests docs/rules/value-types.md docs/getting-started.md
git commit -m "feat: add ZA1503 to report struct hash keys without IEquatable" -m "Closes #81" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 2: ZA1402 — use the state-passing overload (#80)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/UseStatePassingOverloadAnalyzer.cs`
- Create: `src/ZeroAlloc.Analyzers.CodeFixes/UseStatePassingOverloadCodeFixProvider.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA1402_UseStatePassingOverloadTests.cs`
- Modify: `src/ZeroAlloc.Analyzers/DiagnosticIds.cs`, `src/ZeroAlloc.Analyzers/AnalyzerReleases.Unshipped.md`, `docs/rules/delegates.md`, `docs/getting-started.md`

**Interfaces:**
- Produces:
  - `DiagnosticIds.UseStatePassingOverload = "ZA1402"`.
  - `UseStatePassingOverloadAnalyzer.FixProperty = "Fix"`, whose value is `"GetOrAdd"` or `"QueueUserWorkItem"` when a fix applies and is absent otherwise.
  - `UseStatePassingOverloadAnalyzer.CapturedProperty = "Captured"`, the name of the single captured variable.

- [ ] **Step 1: Add the ID constant**

In `DiagnosticIds.cs`, after `public const string UseStaticLambda = "ZA1401";`:

```csharp
    public const string UseStatePassingOverload = "ZA1402";
```

- [ ] **Step 2: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA1402_UseStatePassingOverloadTests.cs`:

```csharp
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1402_UseStatePassingOverloadTests
{
    private static DiagnosticResult Expected(string api, string captured, int location = 0) =>
        CSharpAnalyzerVerifier<UseStatePassingOverloadAnalyzer>
            .Diagnostic(DiagnosticIds.UseStatePassingOverload)
            .WithLocation(location)
            .WithArguments(api, captured);

    [Fact]
    public async Task GetOrAddCapturingParameter_ReportsAndFixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, {|#0:k => prefix + k|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, static (k, prefix) => prefix + k, prefix);
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("ConcurrentDictionary.GetOrAdd", "'prefix'"));
    }

    [Fact]
    public async Task QueueUserWorkItemCapturingLocal_ReportsAndFixes()
    {
        var source = """
            using System.Threading;

            class Work { public void Run() { } }

            class C
            {
                void M()
                {
                    var work = new Work();
                    ThreadPool.QueueUserWorkItem({|#0:_ => work.Run()|});
                }
            }
            """;

        var fixedSource = """
            using System.Threading;

            class Work { public void Run() { } }

            class C
            {
                void M()
                {
                    var work = new Work();
                    ThreadPool.QueueUserWorkItem(static work => work.Run(), work, preferLocal: false);
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("ThreadPool.QueueUserWorkItem", "'work'"));
    }

    [Theory]
    [InlineData("var value = cache.GetOrAdd(1, {|#0:k => prefix + k|}); prefix = \"changed\"; return value;")]
    [InlineData("return cache.GetOrAdd(1, {|#0:(int k) => prefix + k|});")]
    public async Task GetOrAddFixWouldChangeMeaningOrNeedTypes_ReportsWithoutFix(string body)
    {
        // A later write is seen by a closure but not by a state snapshot; a typed lambda would need the state's type spelled out.
        var source = $$"""
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    {{body}}
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected("ConcurrentDictionary.GetOrAdd", "'prefix'"));
    }

    [Fact]
    public async Task QueueUserWorkItemUsingItsParameter_ReportsWithoutFix()
    {
        var source = """
            using System.Threading;

            class Work { public void Run(object state) { } }

            class C
            {
                void M(Work work)
                {
                    ThreadPool.QueueUserWorkItem({|#0:state => work.Run(state)|});
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected("ThreadPool.QueueUserWorkItem", "'work'"));
    }

    [Theory]
    [InlineData("token.Register({|#0:() => other.Cancel()|});", "CancellationToken.Register", "'other'")]
    [InlineData("Task.Factory.StartNew({|#0:() => other.Cancel()|});", "TaskFactory.StartNew", "'other'")]
    [InlineData("_ = string.Create(3, 'x', {|#0:(span, c) => span.Fill(fill)|});", "String.Create", "'fill'")]
    [InlineData("cache.GetOrAdd(1, {|#0:k => (k + _offset + fill).ToString()|});", "ConcurrentDictionary.GetOrAdd", "'this', 'fill'")]
    public async Task OtherApisAndCaptures_Report(string statement, string api, string captured)
    {
        // Object-state APIs, string.Create's existing state, and captures of 'this' or several
        // variables get the diagnostic but no fix.
        var source = $$"""
            using System;
            using System.Collections.Concurrent;
            using System.Threading;
            using System.Threading.Tasks;

            class C
            {
                private int _offset;

                void M(CancellationToken token, CancellationTokenSource other, ConcurrentDictionary<int, string> cache, char fill)
                {
                    {{statement}}
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected(api, captured));
    }

    [Fact]
    public async Task AddOrUpdateWithTwoCapturingLambdas_ReportsBoth()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.AddOrUpdate(1, {|#0:k => prefix|}, {|#1:(k, old) => old + prefix|});
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseStatePassingOverloadAnalyzer>.VerifyAnalyzerAsync(
            source,
            "net8.0",
            Expected("ConcurrentDictionary.AddOrUpdate", "'prefix'", 0),
            Expected("ConcurrentDictionary.AddOrUpdate", "'prefix'", 1));
    }

    [Theory]
    [InlineData("return cache.GetOrAdd(1, static k => k.ToString());")]
    [InlineData("return cache.GetOrAdd(1, k => k.ToString());")]
    [InlineData("return cache.GetOrAdd(1, static (k, p) => p + k, prefix);")]
    [InlineData("return cache.GetOrAdd(1, Make);")]
    [InlineData("return Task.Run(() => prefix).Result;")]
    public async Task NothingCapturedOrApiNotListed_NoDiagnostic(string body)
    {
        // Static or non-capturing lambdas, the state overload already in use, method groups,
        // and APIs with no state-passing overload.
        var source = $$"""
            using System.Collections.Concurrent;
            using System.Threading.Tasks;

            class C
            {
                static string Make(int k) => k.ToString();

                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    {{body}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseStatePassingOverloadAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1402"`
Expected: build errors CS0246 for `UseStatePassingOverloadAnalyzer` and `UseStatePassingOverloadCodeFixProvider`.

- [ ] **Step 4: Write the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/UseStatePassingOverloadAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseStatePassingOverloadAnalyzer : DiagnosticAnalyzer
{
    public const string FixProperty = "Fix";
    public const string CapturedProperty = "Captured";
    public const string GetOrAddFix = "GetOrAdd";
    public const string QueueUserWorkItemFix = "QueueUserWorkItem";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseStatePassingOverload,
        "Use the state-passing overload instead of a capturing lambda",
        "Lambda passed to '{0}' captures {1}; use the overload that passes state so the lambda can be static",
        DiagnosticCategories.Delegates,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // Each API, the name of the state parameter its state-passing overload takes, and the code fix
    // that applies to it, if any. Adding an API here is all it takes to cover it.
    private static readonly (string Type, string Method, string StateParameter, string? Fix)[] ApiTable =
    [
        ("System.Collections.Concurrent.ConcurrentDictionary`2", "GetOrAdd", "factoryArgument", GetOrAddFix),
        ("System.Collections.Concurrent.ConcurrentDictionary`2", "AddOrUpdate", "factoryArgument", null),
        ("System.Threading.CancellationToken", "Register", "state", null),
        ("System.Threading.CancellationToken", "UnsafeRegister", "state", null),
        ("System.Threading.ThreadPool", "QueueUserWorkItem", "state", QueueUserWorkItemFix),
        ("System.Threading.ThreadPool", "UnsafeQueueUserWorkItem", "state", QueueUserWorkItemFix),
        ("System.Threading.Tasks.TaskFactory", "StartNew", "state", null),
        ("System.String", "Create", "state", null),
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var apis = ResolveApis(start.Compilation);
            if (apis.IsEmpty)
                return;

            start.RegisterOperationAction(context => AnalyzeInvocation(context, apis), OperationKind.Invocation);
        });
    }

    private static ImmutableArray<Api> ResolveApis(Compilation compilation)
    {
        var builder = ImmutableArray.CreateBuilder<Api>();
        foreach (var (typeName, methodName, stateParameter, fix) in ApiTable)
        {
            if (compilation.GetTypeByMetadataName(typeName) is not { } type)
                continue;

            var overloads = type.GetMembers(methodName).OfType<IMethodSymbol>().ToImmutableArray();

            // Only APIs whose state-passing overload exists in this compilation.
            if (!overloads.Any(m => m.Parameters.Any(p => p.Name == stateParameter)))
                continue;

            // The QueueUserWorkItem fix needs the generic overload with a typed state, added in .NET Core 3.0.
            var usableFix = fix == QueueUserWorkItemFix && !overloads.Any(m => m.IsGenericMethod) ? null : fix;
            builder.Add(new Api(type, methodName, usableFix));
        }

        return builder.ToImmutable();
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<Api> apis)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        var api = apis.FirstOrDefault(a => a.Method == method.Name
            && SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, a.Type));
        if (api is null)
            return;

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Value is not IDelegateCreationOperation { Target: IAnonymousFunctionOperation lambda }
                || lambda.Symbol.IsStatic)
            {
                continue;
            }

            var captures = Captures(lambda);
            if (captures.Variables.IsEmpty && !captures.This)
                continue;

            var names = captures.Variables.Select(v => $"'{v.Name}'");
            if (captures.This)
                names = names.Prepend("'this'");

            var properties = ImmutableDictionary<string, string?>.Empty;
            if (FixFor(api, invocation, argument, lambda, captures) is { } fix)
            {
                properties = properties
                    .Add(FixProperty, fix)
                    .Add(CapturedProperty, captures.Variables[0].Name);
            }

            context.ReportDiagnostic(Diagnostic.Create(
                Rule,
                lambda.Syntax.GetLocation(),
                properties,
                $"{method.ContainingType.Name}.{method.Name}",
                string.Join(", ", names)));
        }
    }

    // Locals and parameters used inside the lambda but declared outside it, in order of first use,
    // plus whether it uses 'this'.
    private static Captured Captures(IAnonymousFunctionOperation lambda)
    {
        var span = lambda.Syntax.Span;
        var variables = ImmutableArray.CreateBuilder<ISymbol>();
        var capturesThis = false;

        foreach (var operation in lambda.Body.Descendants())
        {
            ISymbol? symbol = operation switch
            {
                ILocalReferenceOperation local => local.Local,
                IParameterReferenceOperation parameter => parameter.Parameter,
                _ => null,
            };

            if (operation is IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance })
                capturesThis = true;

            if (symbol is not null
                && !symbol.DeclaringSyntaxReferences.Any(r => span.Contains(r.Span))
                && !variables.Contains(symbol, SymbolEqualityComparer.Default))
            {
                variables.Add(symbol);
            }
        }

        return new Captured(variables.ToImmutable(), capturesThis);
    }

    // The code fix applies only when the rewrite keeps the meaning: one captured variable that is
    // never written, an implicitly typed lambda, and the overload shape the fix knows how to rewrite.
    private static string? FixFor(Api api, IInvocationOperation invocation, IArgumentOperation argument, IAnonymousFunctionOperation lambda, Captured captures)
    {
        if (api.Fix is null
            || captures.This
            || captures.Variables.Length != 1
            || captures.Variables[0] is not (ILocalSymbol { IsRef: false } or IParameterSymbol { RefKind: RefKind.None })
            || !IsImplicitlyTyped(lambda.Syntax)
            || IsWrittenAnywhere(invocation, captures.Variables[0]))
        {
            return null;
        }

        var method = invocation.TargetMethod;
        return api.Fix switch
        {
            GetOrAddFix when method.Parameters.Length == 2 && argument.Parameter?.Name == "valueFactory" => GetOrAddFix,
            QueueUserWorkItemFix when method.Parameters.Length == 1 && !UsesOwnParameter(lambda) => QueueUserWorkItemFix,
            _ => null,
        };
    }

    private static bool IsImplicitlyTyped(SyntaxNode lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax => true,
        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters.All(p => p.Type is null),
        _ => false,
    };

    private static bool UsesOwnParameter(IAnonymousFunctionOperation lambda) =>
        lambda.Body.Descendants().OfType<IParameterReferenceOperation>()
            .Any(p => SymbolEqualityComparer.Default.Equals(p.Parameter.ContainingSymbol, lambda.Symbol));

    // A closure sees writes made after it is created; a state argument is a snapshot. Any write to the
    // variable in the method body makes the rewrite unsafe.
    private static bool IsWrittenAnywhere(IOperation operation, ISymbol variable)
    {
        var root = operation;
        while (root.Parent is not null)
            root = root.Parent;

        foreach (var reference in root.Descendants())
        {
            var referenced = reference switch
            {
                ILocalReferenceOperation local => local.Local,
                IParameterReferenceOperation parameter => (ISymbol)parameter.Parameter,
                _ => null,
            };

            if (referenced is null || !SymbolEqualityComparer.Default.Equals(referenced, variable))
                continue;

            switch (reference.Parent)
            {
                case ISimpleAssignmentOperation assignment when assignment.Target == reference:
                case ICompoundAssignmentOperation compound when compound.Target == reference:
                case ICoalesceAssignmentOperation coalesce when coalesce.Target == reference:
                case IIncrementOrDecrementOperation:
                case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out }:
                case ITupleOperation { Parent: IDeconstructionAssignmentOperation }:
                    return true;
            }
        }

        return false;
    }

    private sealed class Api(INamedTypeSymbol type, string method, string? fix)
    {
        public INamedTypeSymbol Type { get; } = type;

        public string Method { get; } = method;

        public string? Fix { get; } = fix;
    }

    private sealed class Captured(ImmutableArray<ISymbol> variables, bool capturesThis)
    {
        public ImmutableArray<ISymbol> Variables { get; } = variables;

        public bool This { get; } = capturesThis;
    }
}
```

- [ ] **Step 5: Write the code fix**

Create `src/ZeroAlloc.Analyzers.CodeFixes/UseStatePassingOverloadCodeFixProvider.cs`:

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
public sealed class UseStatePassingOverloadCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.UseStatePassingOverload];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];

        // The analyzer decides whether a rewrite keeps the meaning and says so in the properties.
        if (!diagnostic.Properties.TryGetValue(UseStatePassingOverloadAnalyzer.FixProperty, out var fix) || fix is null
            || !diagnostic.Properties.TryGetValue(UseStatePassingOverloadAnalyzer.CapturedProperty, out var captured) || captured is null)
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not LambdaExpressionSyntax lambda
            || lambda.Parent is not ArgumentSyntax lambdaArgument
            || lambdaArgument.Parent?.Parent is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use the state-passing overload",
                ct => ReplaceAsync(context.Document, invocation, lambdaArgument, lambda, fix, captured, ct),
                equivalenceKey: DiagnosticIds.UseStatePassingOverload),
            diagnostic);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax lambdaArgument,
        LambdaExpressionSyntax lambda,
        string fix,
        string captured,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var body = lambda.Body.ToFullString().Trim();
        string newLambda;
        if (fix == UseStatePassingOverloadAnalyzer.GetOrAddFix)
        {
            // k => ...  becomes  static (k, captured) => ...
            newLambda = $"static ({FirstParameterName(lambda)}, {captured}) => {body}";
        }
        else
        {
            // _ => ...  becomes  static captured => ...
            newLambda = $"static {captured} => {body}";
        }

        var arguments = new List<string>();
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            arguments.Add(argument == lambdaArgument
                ? (argument.NameColon is { } name ? $"{name.ToString().Trim()} " : string.Empty) + newLambda
                : argument.ToString());
        }

        arguments.Add(captured);
        if (fix == UseStatePassingOverloadAnalyzer.QueueUserWorkItemFix)
            arguments.Add("preferLocal: false");

        var newArgumentList = SyntaxFactory.ParseArgumentList($"({string.Join(", ", arguments)})")
            .WithTriviaFrom(invocation.ArgumentList);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, invocation.WithArgumentList(newArgumentList)));
    }

    private static string FirstParameterName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters[0].Identifier.Text,
        _ => "_",
    };
}
```

A block-bodied lambda keeps its body text, including its line breaks, because the body is copied with `ToFullString()`.

- [ ] **Step 6: Add the release-tracking row**

Append to the `### New Rules` table:

```
ZA1402  | Performance.Delegates   | Info     | UseStatePassingOverloadAnalyzer
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release --filter "FullyQualifiedName~ZA1402"`
Expected: PASS, 15 tests.

- [ ] **Step 8: Document the rule**

In `docs/rules/delegates.md`:

1. Change the intro sentence "The ZA14xx rules help you eliminate unnecessary delegate allocations by using `static` lambdas where no capture is needed." to "The ZA14xx rules help you eliminate unnecessary delegate allocations: use `static` lambdas where no capture is needed, and pass state through the API instead of capturing it where the API allows."
2. In the mermaid flowchart, replace the `Closure` node line with these two lines:

```
    L -->|Yes — captures instance,\nlocal, or loop variable| Q{Does the API take\na state argument?}
    Q -->|Yes| State["Pass the value as state,\nmake the lambda static\nZA1402"]
    Q -->|No| Closure["New closure object\nallocated per call site invocation"]
```

3. Append this section at the end of the file:

````markdown

---

## ZA1402 — Use the state-passing overload instead of a capturing lambda {#za1402}

> **Severity**: Info | **Min TFM**: Any | **Code fix**: Yes, for `GetOrAdd` and `QueueUserWorkItem`

### Why

A lambda that captures a local, a parameter or `this` allocates a closure object and a new delegate every time the enclosing code runs. Several BCL APIs have an overload that takes the value as a state argument and hands it back to the callback. With that overload the lambda captures nothing, so it can be `static` and the compiler caches a single delegate.

The rule covers `ConcurrentDictionary.GetOrAdd` and `AddOrUpdate`, `CancellationToken.Register` and `UnsafeRegister`, `ThreadPool.QueueUserWorkItem` and `UnsafeQueueUserWorkItem`, `TaskFactory.StartNew`, and `string.Create`, wherever the state-passing overload exists for your target framework. Inside a loop, [ZA0502](boxing.md#za0502) reports the same lambda as a closure in a loop; ZA1402 tells you which overload removes it.

The code fix handles `GetOrAdd` and `QueueUserWorkItem`, whose state is typed. It only runs when exactly one local or parameter is captured, that variable is never assigned again, and the lambda's parameters have no explicit types: a closure sees later writes, while a state argument is a copy taken at the call. For `QueueUserWorkItem` the fix passes `preferLocal: false`, which matches how the original overload queues work. `Register` and `StartNew` take an `object` state, so you cast it back inside the lambda yourself.

### Before

```csharp
// ❌ allocates a closure and a delegate on every call
public Widget Get(int id, WidgetFactory factory) =>
    _cache.GetOrAdd(id, key => factory.Create(key));
```

### After

```csharp
// ✓ the static lambda is cached; the factory travels as state
public Widget Get(int id, WidgetFactory factory) =>
    _cache.GetOrAdd(id, static (key, factory) => factory.Create(key), factory);
```

### Suppression

```csharp
#pragma warning disable ZA1402
// or in .editorconfig: dotnet_diagnostic.ZA1402.severity = none
```
````

In `docs/getting-started.md`, add after the ZA1401 row in the Delegates table:

```markdown
| [ZA1402](rules/delegates.md#za1402) | Use the state-passing overload instead of a capturing lambda | Info | Any |
```

- [ ] **Step 9: Commit**

```bash
git add src tests docs/rules/delegates.md docs/getting-started.md
git commit -m "feat: add ZA1402 to suggest state-passing overloads over capturing lambdas" -m "Closes #80" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

---

### Task 3: Rule counts and spec refinements

**Files:**
- Modify: `README.md`, `docs/getting-started.md`, `docs/performance.md`, `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`

- [ ] **Step 1: Update rule counts**

Two rules take the total from 57 to 59: Delegates 1 to 2, Value Types 2 to 3. Both rules are active on every target framework, because `Dictionary` and at least one state-passing API exist even on `netstandard2.0`.

- `README.md` and `docs/getting-started.md`: replace every `57 rules` with `59 rules`.
- `docs/getting-started.md` mermaid graph: `ZA14xx (1 rule)` becomes `ZA14xx (2 rules)`, and `ZA15xx (2 rules)` becomes `ZA15xx (3 rules)`.
- `README.md` documentation table:
  - Delegates row: `Static lambda caching, closure elimination, state-passing overloads`.
  - Value Types row: `Struct GetHashCode override and IEquatable on hash keys, avoid finalizers`.
- `README.md` performance table and `docs/performance.md`: add 2 to every "active" count and change every "of 57" to "of 59". `55 of 57` becomes `57 of 59`, `38 of 57, or 40 with the System.Memory package` becomes `40 of 59, or 42 with the System.Memory package`, the multi-TFM row's `55 / 38` becomes `57 / 40` (keep any package note), and `53 of 57` becomes `55 of 59`. Leave the timing columns unchanged.

Run: `grep -rnE "57 rules|of 57|\b55 / 38\b" README.md docs --include=*.md | grep -v "docs/plans/"`
Expected: no output.

- [ ] **Step 2: Record the refinements in the design doc**

In `docs/plans/2026-10-09-zero-alloc-aot-batch-3-design.md`:

- Under **ZA1402 → Code fix**, replace the text with: `Offered for ConcurrentDictionary.GetOrAdd and ThreadPool.QueueUserWorkItem/UnsafeQueueUserWorkItem, whose state is typed. It requires exactly one captured local or parameter, with no this, no ref local and no ref or out parameter; the captured variable is never written in its declaring body; the lambda is implicitly typed; and for QueueUserWorkItem, the lambda does not use its own parameter. QueueUserWorkItem is rewritten to the generic overload with preferLocal: false. Object-state APIs, AddOrUpdate and string.Create get the diagnostic only.`
- Under **ZA1402 → Detection**, add: `Inside a loop the same lambda is also reported by ZA0502; ZA1402 gives the API-specific fix.`
- Under **ZA1503 → Detection**, add: `A call counts as passing a comparer only when an argument supplies a non-null IEqualityComparer<T>; an optional comparer parameter left at its default does not count. Collection expressions are not covered.`

- [ ] **Step 3: Run the full suite and build**

Run: `dotnet build -c Release && dotnet test tests/ZeroAlloc.Analyzers.Tests -c Release`
Expected: build succeeds with 0 warnings; all tests pass.

- [ ] **Step 4: Commit**

```bash
git add README.md docs
git commit -m "docs: update rule counts for ZA1402 and ZA1503 and record their design refinements" -m "Co-Authored-By: <your model> <noreply@anthropic.com>"
```

Push and PR are done by the controller after the final review. The PR body carries:

```
BEGIN_COMMIT_OVERRIDE
feat: add ZA1402 to suggest state-passing overloads over capturing lambdas
feat: add ZA1503 to report struct hash keys without IEquatable
END_COMMIT_OVERRIDE
```
