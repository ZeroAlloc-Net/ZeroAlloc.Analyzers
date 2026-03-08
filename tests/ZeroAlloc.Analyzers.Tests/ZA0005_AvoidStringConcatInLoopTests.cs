using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0005_AvoidStringConcatInLoopTests
{
    [Fact]
    public async Task StringConcatInForLoop_Reports()
    {
        var source = """
            class C
            {
                string M()
                {
                    var result = "";
                    for (int i = 0; i < 10; i++)
                    {
                        {|#0:result += i.ToString()|};
                    }
                    return result;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringConcatInLoop)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringConcatInForeachLoop_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                string M(List<string> items)
                {
                    var result = "";
                    foreach (var item in items)
                    {
                        {|#0:result += item|};
                    }
                    return result;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringConcatInLoop)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringConcatOutsideLoop_NoDiagnostic()
    {
        var source = """
            class C
            {
                string M()
                {
                    var result = "hello" + " " + "world";
                    return result;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }
}
