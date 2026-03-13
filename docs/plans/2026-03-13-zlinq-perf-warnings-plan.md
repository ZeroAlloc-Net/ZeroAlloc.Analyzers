# ZLinq-Inspired Performance Warnings Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add three new Roslyn analyzer rules (ZA0108, ZA0208, ZA0606) inspired by performance pitfalls documented in Cysharp/ZLinq, each with tests and one with a code fix.

**Architecture:** Each rule follows the existing ZeroAlloc pattern — a `DiagnosticAnalyzer` in `src/ZeroAlloc.Analyzers/Analyzers/`, optional `CodeFixProvider` in `src/ZeroAlloc.Analyzers.CodeFixes/`, and test file in `tests/ZeroAlloc.Analyzers.Tests/`. Tests use raw string literals and the `CSharpAnalyzerVerifier<T>` / `CSharpCodeFixVerifier<T,F>` helpers already in the test project.

**Tech Stack:** Roslyn (Microsoft.CodeAnalysis), xUnit, Microsoft.CodeAnalysis.Testing

---

## Task 1: Register the three new diagnostic IDs

**Files:**
- Modify: `src/ZeroAlloc.Analyzers/DiagnosticIds.cs`

**Step 1: Add the three new constants**

Open `src/ZeroAlloc.Analyzers/DiagnosticIds.cs` and add:

```csharp
// In the ZA01xx — Collections section:
public const string AvoidRedundantMaterialization = "ZA0108";

// In the ZA02xx — Strings section:
public const string AvoidStringJoinBoxingOverload = "ZA0208";

// In the ZA06xx — LINQ & Params section:
public const string AvoidForeachOverInterfaceCollection = "ZA0606";
```

**Step 2: Build to verify no compile errors**

```bash
dotnet build src/ZeroAlloc.Analyzers/ZeroAlloc.Analyzers.csproj
```

Expected: Build succeeded, 0 errors.

**Step 3: Commit**

```bash
git add src/ZeroAlloc.Analyzers/DiagnosticIds.cs
git commit -m "feat: register diagnostic IDs ZA0108, ZA0208, ZA0606"
```

---

## Task 2: Implement ZA0606 — AvoidForeachOverInterfaceCollection (analyzer)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/AvoidForeachOverInterfaceCollectionAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0606_AvoidForeachOverInterfaceCollectionTests.cs`

**Step 1: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0606_AvoidForeachOverInterfaceCollectionTests.cs`:

```csharp
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0606_AvoidForeachOverInterfaceCollectionTests
{
    [Fact]
    public async Task LocalVarIEnumerable_InitWithNewList_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    IEnumerable<int> {|#0:items|} = new List<int> { 1, 2, 3 };
                    foreach (var item in items) { }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidForeachOverInterfaceCollection)
            .WithLocation(0)
            .WithArguments("items", "IEnumerable<int>");

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LocalVarICollection_InitWithNewList_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    ICollection<int> {|#0:items|} = new List<int> { 1, 2, 3 };
                    foreach (var item in items) { }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidForeachOverInterfaceCollection)
            .WithLocation(0)
            .WithArguments("items", "ICollection<int>");

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LocalVarIList_InitWithArrayInit_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    IList<int> {|#0:items|} = new int[] { 1, 2, 3 };
                    foreach (var item in items) { }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidForeachOverInterfaceCollection)
            .WithLocation(0)
            .WithArguments("items", "IList<int>");

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LocalVarIReadOnlyCollection_InitWithNewList_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    IReadOnlyCollection<int> {|#0:items|} = new List<int> { 1, 2, 3 };
                    foreach (var item in items) { }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidForeachOverInterfaceCollection)
            .WithLocation(0)
            .WithArguments("items", "IReadOnlyCollection<int>");

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task MethodParameter_IEnumerable_NoDiagnostic()
    {
        // Method parameters from callers can't be changed — don't flag
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(IEnumerable<int> items)
                {
                    foreach (var item in items) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LocalVarConcreteList_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    List<int> items = new List<int> { 1, 2, 3 };
                    foreach (var item in items) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LocalVarArray_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    int[] items = new int[] { 1, 2, 3 };
                    foreach (var item in items) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LocalVarIEnumerable_InitFromMethodCall_NoDiagnostic()
    {
        // Can't change — the concrete type is unknown at declaration site
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M()
                {
                    IEnumerable<int> items = Enumerable.Range(1, 10);
                    foreach (var item in items) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LocalVarIEnumerable_NotUsedInForeach_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M()
                {
                    IEnumerable<int> items = new List<int> { 1, 2, 3 };
                    var count = items.Count();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
```

**Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/ZeroAlloc.Analyzers.Tests/ --filter "ZA0606" --no-build 2>&1 | tail -5
```

Expected: errors about `AvoidForeachOverInterfaceCollectionAnalyzer` not found.

**Step 3: Implement the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/AvoidForeachOverInterfaceCollectionAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidForeachOverInterfaceCollectionAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidForeachOverInterfaceCollection,
        "Avoid foreach over interface-typed local collection",
        "Local variable '{0}' is typed as '{1}' — foreach allocates a heap enumerator; use a concrete type (List<T>, T[]) instead",
        DiagnosticCategories.Linq,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ImmutableHashSet<string> CollectionInterfaces = ImmutableHashSet.Create(
        "IEnumerable`1",
        "ICollection`1",
        "IList`1",
        "IReadOnlyCollection`1",
        "IReadOnlyList`1");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeForeach, SyntaxKind.ForEachStatement);
    }

    private static void AnalyzeForeach(SyntaxNodeAnalysisContext context)
    {
        var forEach = (ForEachStatementSyntax)context.Node;

        // Get the symbol for the foreach expression
        var exprType = context.SemanticModel.GetTypeInfo(forEach.Expression, context.CancellationToken).Type;
        if (exprType is not INamedTypeSymbol namedType)
            return;

        // Must be one of the target interfaces
        if (!IsCollectionInterface(namedType))
            return;

        // The expression must be a simple identifier (local variable reference)
        if (forEach.Expression is not IdentifierNameSyntax identifier)
            return;

        var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
        if (symbol is not ILocalSymbol localSymbol)
            return;

        // Find the variable declarator to check the initializer
        var declaratorRef = localSymbol.DeclaringSyntaxReferences;
        if (declaratorRef.IsEmpty)
            return;

        var declaratorSyntax = declaratorRef[0].GetSyntax(context.CancellationToken);
        if (declaratorSyntax is not VariableDeclaratorSyntax declarator)
            return;

        // Only flag if initialized with a concrete new expression or array creation
        var initializer = declarator.Initializer?.Value;
        if (initializer is null)
            return;

        if (!IsConcreteNewExpression(initializer))
            return;

        var typeName = namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, declarator.Identifier.GetLocation(),
                localSymbol.Name, typeName));
    }

    private static bool IsCollectionInterface(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Interface
           && type.IsGenericType
           && CollectionInterfaces.Contains(type.MetadataName);

    private static bool IsConcreteNewExpression(ExpressionSyntax expr)
        => expr is ObjectCreationExpressionSyntax
            or ImplicitObjectCreationExpressionSyntax
            or ArrayCreationExpressionSyntax
            or ImplicitArrayCreationExpressionSyntax
            or CollectionExpressionSyntax;
}
```

**Step 4: Build and run tests**

```bash
dotnet build src/ZeroAlloc.Analyzers/ZeroAlloc.Analyzers.csproj && dotnet test tests/ZeroAlloc.Analyzers.Tests/ --filter "ZA0606" -v minimal
```

Expected: All ZA0606 tests pass.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Analyzers/Analyzers/AvoidForeachOverInterfaceCollectionAnalyzer.cs \
        tests/ZeroAlloc.Analyzers.Tests/ZA0606_AvoidForeachOverInterfaceCollectionTests.cs
git commit -m "feat: add ZA0606 AvoidForeachOverInterfaceCollection analyzer"
```

---

## Task 3: Implement ZA0208 — AvoidStringJoinBoxingOverload (analyzer)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/AvoidStringJoinBoxingOverloadAnalyzer.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0208_AvoidStringJoinBoxingOverloadTests.cs`

**Step 1: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0208_AvoidStringJoinBoxingOverloadTests.cs`:

```csharp
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0208_AvoidStringJoinBoxingOverloadTests
{
    [Fact]
    public async Task StringJoin_ListOfInt_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var numbers = new List<int> { 1, 2, 3 };
                    var result = string.{|#0:Join|}(", ", numbers);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringJoinBoxingOverload)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringJoin_ArrayOfInt_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    int[] numbers = new int[] { 1, 2, 3 };
                    var result = string.{|#0:Join|}(", ", numbers);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringJoinBoxingOverload)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringJoin_ListOfString_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var words = new List<string> { "a", "b" };
                    var result = string.Join(", ", words);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StringJoin_ArrayOfString_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    string[] words = new string[] { "a", "b" };
                    var result = string.Join(", ", words);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StringJoin_CharSeparator_ListOfInt_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var numbers = new List<int> { 1, 2, 3 };
                    var result = string.{|#0:Join|}(',', numbers);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringJoinBoxingOverload)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }
}
```

**Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/ZeroAlloc.Analyzers.Tests/ --filter "ZA0208" --no-build 2>&1 | tail -5
```

Expected: errors about `AvoidStringJoinBoxingOverloadAnalyzer` not found.

**Step 3: Implement the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/AvoidStringJoinBoxingOverloadAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidStringJoinBoxingOverloadAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidStringJoinBoxingOverload,
        "Avoid string.Join resolving to params object[] overload",
        "string.Join resolves to the 'params object[]' overload — each element is boxed; use .Select(x => x.ToString()) or cast to IEnumerable<string>",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "Join")
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        // Must be string.Join
        if (method.ContainingType?.SpecialType != SpecialType.System_String)
            return;

        // Check if the resolved overload uses params object[]
        // Signature: Join(string/char separator, params object[] values)
        if (method.Parameters.Length < 2)
            return;

        var lastParam = method.Parameters[method.Parameters.Length - 1];
        if (!lastParam.IsParams)
            return;

        if (lastParam.Type is not IArrayTypeSymbol arrayType)
            return;

        if (arrayType.ElementType.SpecialType != SpecialType.System_Object)
            return;

        context.ReportDiagnostic(
            Diagnostic.Create(Rule, memberAccess.Name.GetLocation()));
    }
}
```

**Step 4: Build and run tests**

```bash
dotnet build src/ZeroAlloc.Analyzers/ZeroAlloc.Analyzers.csproj && dotnet test tests/ZeroAlloc.Analyzers.Tests/ --filter "ZA0208" -v minimal
```

Expected: All ZA0208 tests pass.

**Step 5: Commit**

```bash
git add src/ZeroAlloc.Analyzers/Analyzers/AvoidStringJoinBoxingOverloadAnalyzer.cs \
        tests/ZeroAlloc.Analyzers.Tests/ZA0208_AvoidStringJoinBoxingOverloadTests.cs
git commit -m "feat: add ZA0208 AvoidStringJoinBoxingOverload analyzer"
```

---

## Task 4: Implement ZA0108 — AvoidRedundantMaterialization (analyzer + code fix)

**Files:**
- Create: `src/ZeroAlloc.Analyzers/Analyzers/AvoidRedundantMaterializationAnalyzer.cs`
- Create: `src/ZeroAlloc.Analyzers.CodeFixes/AvoidRedundantMaterializationCodeFixProvider.cs`
- Create: `tests/ZeroAlloc.Analyzers.Tests/ZA0108_AvoidRedundantMaterializationTests.cs`

**Step 1: Write the failing tests**

Create `tests/ZeroAlloc.Analyzers.Tests/ZA0108_AvoidRedundantMaterializationTests.cs`:

```csharp
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0108_AvoidRedundantMaterializationTests
{
    // --- Analyzer tests ---

    [Fact]
    public async Task ToList_OnList_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M()
                {
                    List<int> items = new List<int> { 1, 2, 3 };
                    var copy = items.{|#0:ToList|}();
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidRedundantMaterializationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidRedundantMaterialization)
            .WithLocation(0)
            .WithArguments("items", "List<int>", "ToList");

        await CSharpAnalyzerVerifier<AvoidRedundantMaterializationAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task ToArray_OnArray_Reports()
    {
        var source = """
            using System.Linq;

            class C
            {
                void M()
                {
                    int[] items = new int[] { 1, 2, 3 };
                    var copy = items.{|#0:ToArray|}();
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidRedundantMaterializationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidRedundantMaterialization)
            .WithLocation(0)
            .WithArguments("items", "int[]", "ToArray");

        await CSharpAnalyzerVerifier<AvoidRedundantMaterializationAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task ToList_OnIEnumerable_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(IEnumerable<int> items)
                {
                    var list = items.ToList();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidRedundantMaterializationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ToArray_OnIEnumerable_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(IEnumerable<int> items)
                {
                    var arr = items.ToArray();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidRedundantMaterializationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ToArray_OnList_NoDiagnostic()
    {
        // ToArray on List IS useful (different type)
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M()
                {
                    List<int> items = new List<int> { 1, 2, 3 };
                    var arr = items.ToArray();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidRedundantMaterializationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    // --- Code fix tests ---

    [Fact]
    public async Task ToList_OnList_CodeFix_RemovesCall()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M()
                {
                    List<int> items = new List<int> { 1, 2, 3 };
                    var copy = items.{|#0:ToList|}();
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M()
                {
                    List<int> items = new List<int> { 1, 2, 3 };
                    var copy = items;
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidRedundantMaterializationAnalyzer, AvoidRedundantMaterializationCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, DiagnosticIds.AvoidRedundantMaterialization);
    }

    [Fact]
    public async Task ToArray_OnArray_CodeFix_RemovesCall()
    {
        var source = """
            using System.Linq;

            class C
            {
                void M()
                {
                    int[] items = new int[] { 1, 2, 3 };
                    var copy = items.{|#0:ToArray|}();
                }
            }
            """;

        var fixedSource = """
            using System.Linq;

            class C
            {
                void M()
                {
                    int[] items = new int[] { 1, 2, 3 };
                    var copy = items;
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidRedundantMaterializationAnalyzer, AvoidRedundantMaterializationCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, DiagnosticIds.AvoidRedundantMaterialization);
    }
}
```

**Step 2: Run tests to confirm they fail**

```bash
dotnet test tests/ZeroAlloc.Analyzers.Tests/ --filter "ZA0108" --no-build 2>&1 | tail -5
```

Expected: errors about `AvoidRedundantMaterializationAnalyzer` not found.

**Step 3: Implement the analyzer**

Create `src/ZeroAlloc.Analyzers/Analyzers/AvoidRedundantMaterializationAnalyzer.cs`:

```csharp
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidRedundantMaterializationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidRedundantMaterialization,
        "Avoid redundant ToList/ToArray on already-materialized collection",
        "'{0}' is already a {1} — '{2}' allocates a new collection unnecessarily; use the original directly",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;
        if (methodName != "ToList" && methodName != "ToArray")
            return;

        // Verify it's System.Linq.Enumerable.ToList/ToArray
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        var containingType = method.ReducedFrom?.ContainingType ?? method.ContainingType;
        if (containingType?.Name != "Enumerable"
            || containingType.ContainingNamespace?.ToDisplayString() != "System.Linq")
            return;

        // Get receiver type
        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (receiverType is null)
            return;

        // ToList on List<T> → redundant
        if (methodName == "ToList" && IsListT(receiverType))
        {
            var receiverName = memberAccess.Expression.ToString();
            var typeName = receiverType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, memberAccess.Name.GetLocation(),
                    receiverName, typeName, methodName));
        }

        // ToArray on T[] → redundant
        if (methodName == "ToArray" && receiverType is IArrayTypeSymbol { Rank: 1 })
        {
            var receiverName = memberAccess.Expression.ToString();
            var typeName = receiverType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, memberAccess.Name.GetLocation(),
                    receiverName, typeName, methodName));
        }
    }

    private static bool IsListT(ITypeSymbol type)
        => type is INamedTypeSymbol { IsGenericType: true } named
           && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>";
}
```

**Step 4: Implement the code fix**

Create `src/ZeroAlloc.Analyzers.CodeFixes/AvoidRedundantMaterializationCodeFixProvider.cs`:

```csharp
using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AvoidRedundantMaterializationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.AvoidRedundantMaterialization];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        // Walk up to the full invocation expression (receiver.ToList())
        var invocation = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove redundant materialization call",
                ct => RemoveMaterializationCallAsync(context.Document, invocation, ct),
                equivalenceKey: DiagnosticIds.AvoidRedundantMaterialization),
            diagnostic);
    }

    private static async Task<Document> RemoveMaterializationCallAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

        // Replace items.ToList() with items
        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var receiver = memberAccess.Expression.WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, receiver);
        return document.WithSyntaxRoot(newRoot);
    }
}
```

**Step 5: Build and run tests**

```bash
dotnet build src/ && dotnet test tests/ZeroAlloc.Analyzers.Tests/ --filter "ZA0108" -v minimal
```

Expected: All ZA0108 tests pass (including code fix tests).

**Step 6: Commit**

```bash
git add src/ZeroAlloc.Analyzers/Analyzers/AvoidRedundantMaterializationAnalyzer.cs \
        src/ZeroAlloc.Analyzers.CodeFixes/AvoidRedundantMaterializationCodeFixProvider.cs \
        tests/ZeroAlloc.Analyzers.Tests/ZA0108_AvoidRedundantMaterializationTests.cs
git commit -m "feat: add ZA0108 AvoidRedundantMaterialization analyzer and code fix"
```

---

## Task 5: Run full test suite and verify

**Step 1: Run all tests**

```bash
dotnet test tests/ZeroAlloc.Analyzers.Tests/ -v minimal
```

Expected: All tests pass, 0 failures.

**Step 2: Commit if any fixes were needed, then done**

```bash
git status
```

If clean, you're done. The three new rules are fully implemented and tested.
