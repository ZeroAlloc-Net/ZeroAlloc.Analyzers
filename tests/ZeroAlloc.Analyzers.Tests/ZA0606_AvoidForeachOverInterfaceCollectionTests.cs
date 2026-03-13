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
                    IEnumerable<int> items = new List<int> { 1, 2, 3 };
                    foreach (var item in {|#0:items|}) { }
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
                    ICollection<int> items = new List<int> { 1, 2, 3 };
                    foreach (var item in {|#0:items|}) { }
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
                    IList<int> items = new int[] { 1, 2, 3 };
                    foreach (var item in {|#0:items|}) { }
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
                    IReadOnlyCollection<int> items = new List<int> { 1, 2, 3 };
                    foreach (var item in {|#0:items|}) { }
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
    public async Task LocalVarIReadOnlyList_InitWithNewList_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    IReadOnlyList<int> items = new List<int> { 1, 2, 3 };
                    foreach (var item in {|#0:items|}) { }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidForeachOverInterfaceCollection)
            .WithLocation(0)
            .WithArguments("items", "IReadOnlyList<int>");

        await CSharpAnalyzerVerifier<AvoidForeachOverInterfaceCollectionAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LocalVarIList_InitWithImplicitNew_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    IList<int> items = new List<int> { 1, 2, 3 };
                    foreach (var item in {|#0:items|}) { }
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
    public async Task MethodParameter_IEnumerable_NoDiagnostic()
    {
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
