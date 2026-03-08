using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0003_UseCollectionsMarshalAsSpanTests
{
    [Fact]
    public async Task ForeachOverList_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var list = new List<int> { 1, 2, 3 };
                    {|#0:foreach|} (var item in list)
                    {
                        _ = item;
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<UseCollectionsMarshalAsSpanAnalyzer>
            .Diagnostic(DiagnosticIds.UseCollectionsMarshalAsSpan)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<UseCollectionsMarshalAsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task ForeachOverList_OnNet48_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var list = new List<int> { 1, 2, 3 };
                    foreach (var item in list)
                    {
                        _ = item;
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseCollectionsMarshalAsSpanAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net48");
    }

    [Fact]
    public async Task ForeachOverArray_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = new int[] { 1, 2, 3 };
                    foreach (var item in arr)
                    {
                        _ = item;
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseCollectionsMarshalAsSpanAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task ForeachOverList_WithAwait_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Threading.Tasks;

            class C
            {
                async Task M()
                {
                    var list = new List<int> { 1, 2, 3 };
                    foreach (var item in list)
                    {
                        await Task.Delay(1);
                    }
                }
            }
            """;

        // Span can't cross await boundaries (CS4007)
        await CSharpAnalyzerVerifier<UseCollectionsMarshalAsSpanAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }
}
