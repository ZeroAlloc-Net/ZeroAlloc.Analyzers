using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0009_AvoidStringReplaceChainTests
{
    [Fact]
    public async Task ThreeChainedReplace_Reports()
    {
        var source = """
            class C
            {
                string M(string s) =>
                    {|#0:s.Replace("a", "1").Replace("b", "2").Replace("c", "3")|};
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringReplaceChainAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringReplaceChain)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringReplaceChainAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task TwoReplace_NoDiagnostic()
    {
        var source = """
            class C
            {
                string M(string s) =>
                    s.Replace("a", "1").Replace("b", "2");
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringReplaceChainAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task SingleReplace_NoDiagnostic()
    {
        var source = """
            class C
            {
                string M(string s) => s.Replace("a", "1");
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringReplaceChainAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }
}
