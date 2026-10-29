using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0201_AvoidStringConcatInLoopTests
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

    [Fact]
    public async Task LoopHeaderForeachCollection_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(int n)
                {
                    var result = "";
                    foreach (var c in (result += "x"))
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForeachCollectionInsideOuterLoop_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(int n)
                {
                    var result = "";
                    for (int j = 0; j < 10; j++)
                    {
                        foreach (var c in ({|#0:result += "x"|}))
                        {
                        }
                    }
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
    public async Task LoopHeaderForInitializer_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(int n)
                {
                    var result = "";
                    for (result += "x"; n < 0; n++)
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForCondition_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(int n)
                {
                    var result = "";
                    for (int i = 0; ({|#0:result += "x"|}).Length < 3; i++)
                    {
                    }
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
    public async Task DeconstructingForeachBody_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(int n, Dictionary<int, int> map)
                {
                    var result = "";
                    foreach (var (k, v) in map)
                    {
                        {|#0:result += "x"|};
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringConcatInLoop)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringConcatInLoopAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }
}
