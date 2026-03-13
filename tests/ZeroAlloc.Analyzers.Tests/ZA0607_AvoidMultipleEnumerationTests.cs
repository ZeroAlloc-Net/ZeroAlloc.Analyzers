using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0607_AvoidMultipleEnumerationTests
{
    [Fact]
    public async Task IEnumerableLocalForeachTwice_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(IEnumerable<int> source)
                {
                    IEnumerable<int> query = source;
                    foreach (var x in query) { }
                    foreach (var x in {|#0:query|}) { }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidMultipleEnumeration)
            .WithLocation(0)
            .WithArguments("query");

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task IEnumerableLocalForeachThreeTimes_ReportsTwoWarnings()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(IEnumerable<int> source)
                {
                    IEnumerable<int> query = source;
                    foreach (var x in query) { }
                    foreach (var x in {|#0:query|}) { }
                    foreach (var x in {|#1:query|}) { }
                }
            }
            """;

        var expected0 = CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidMultipleEnumeration)
            .WithLocation(0)
            .WithArguments("query");

        var expected1 = CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidMultipleEnumeration)
            .WithLocation(1)
            .WithArguments("query");

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected0, expected1);
    }

    [Fact]
    public async Task IEnumerableLocalForeachOnce_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(IEnumerable<int> source)
                {
                    IEnumerable<int> query = source;
                    foreach (var x in query) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ListLocalForeachTwice_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    List<int> items = new List<int> { 1, 2, 3 };
                    foreach (var x in items) { }
                    foreach (var x in items) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ArrayLocalForeachTwice_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    int[] items = new int[] { 1, 2, 3 };
                    foreach (var x in items) { }
                    foreach (var x in items) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task MethodParameterIEnumerableForeachTwice_NoDiagnostic()
    {
        // Parameters are excluded — caller controls the type
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(IEnumerable<int> items)
                {
                    foreach (var x in items) { }
                    foreach (var x in items) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task IQueryableLocalForeachTwice_Reports()
    {
        var source = """
            using System.Linq;

            class C
            {
                void M(IQueryable<int> source)
                {
                    IQueryable<int> query = source;
                    foreach (var x in query) { }
                    foreach (var x in {|#0:query|}) { }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidMultipleEnumeration)
            .WithLocation(0)
            .WithArguments("query");

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task IEnumerableLocalForeachInNestedLocalFunction_NoDiagnostic()
    {
        // A foreach inside a nested local function should not be grouped with
        // foreaches in the outer method body.
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(IEnumerable<int> source)
                {
                    IEnumerable<int> query = source;
                    foreach (var x in query) { }

                    void Inner()
                    {
                        foreach (var y in query) { }
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task TwoDifferentLocals_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(IEnumerable<int> source)
                {
                    IEnumerable<int> q1 = source;
                    IEnumerable<int> q2 = source;
                    foreach (var x in q1) { }
                    foreach (var x in q2) { }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidMultipleEnumerationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
