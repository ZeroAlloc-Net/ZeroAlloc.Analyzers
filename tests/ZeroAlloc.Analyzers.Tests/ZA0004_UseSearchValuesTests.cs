using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0004_UseSearchValuesTests
{
    [Fact]
    public async Task FourChainedStringEquals_Reports()
    {
        var source = """
            class C
            {
                bool M(string s) =>
                    {|#0:s == "a" || s == "b" || s == "c" || s == "d"|};
            }
            """;

        var expected = CSharpAnalyzerVerifier<UseSearchValuesAnalyzer>
            .Diagnostic(DiagnosticIds.UseSearchValues)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<UseSearchValuesAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task ThreeChainedEquals_NoDiagnostic()
    {
        var source = """
            class C
            {
                bool M(string s) =>
                    s == "a" || s == "b" || s == "c";
            }
            """;

        await CSharpAnalyzerVerifier<UseSearchValuesAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task FourChainedEquals_OnNet6_NoDiagnostic()
    {
        var source = """
            class C
            {
                bool M(string s) =>
                    s == "a" || s == "b" || s == "c" || s == "d";
            }
            """;

        await CSharpAnalyzerVerifier<UseSearchValuesAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net6.0");
    }
}
