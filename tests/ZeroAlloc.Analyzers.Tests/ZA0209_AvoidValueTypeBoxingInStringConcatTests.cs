using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0209_AvoidValueTypeBoxingInStringConcatTests
{
    [Fact]
    public async Task StringPlusInt_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    int count = 42;
                    var s = "Count: " {|#0:+|} count;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(0)
            .WithArguments("int");

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task IntPlusString_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    int count = 42;
                    var s = count {|#0:+|} " items";
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(0)
            .WithArguments("int");

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringPlusEnum_Reports()
    {
        var source = """
            class C
            {
                enum Status { Active }

                void M()
                {
                    var s = "Status: " {|#0:+|} Status.Active;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(0)
            .WithArguments("Status");

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringPlusString_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var s = "Hello" + " World";
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StringPlusToString_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    int count = 42;
                    var s = "Count: " + count.ToString();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task IntPlusInt_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    int a = 1, b = 2;
                    var c = a + b;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StringInterpolation_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    int count = 42;
                    var s = $"Count: {count}";
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
