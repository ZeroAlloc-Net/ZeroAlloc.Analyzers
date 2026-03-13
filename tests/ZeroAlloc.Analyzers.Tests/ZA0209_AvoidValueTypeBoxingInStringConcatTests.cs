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

    [Fact]
    public async Task ChainedConcatWithTwoValueTypes_ReportsTwice()
    {
        var source = """
            class C
            {
                void M()
                {
                    int a = 1, b = 2;
                    var s = "x" {|#0:+|} a {|#1:+|} b;
                }
            }
            """;

        var expected0 = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(0)
            .WithArguments("int");

        var expected1 = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(1)
            .WithArguments("int");

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected0, expected1);
    }

    [Fact]
    public async Task StringPlusStructWithUserDefinedOperator_NoDiagnostic()
    {
        var source = """
            class C
            {
                struct MyStruct
                {
                    public static string operator+(string s, MyStruct v) => s + "x";
                }

                void M()
                {
                    var v = new MyStruct();
                    var s = "hello" + v;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
