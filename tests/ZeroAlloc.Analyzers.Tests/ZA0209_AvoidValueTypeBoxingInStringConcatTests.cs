using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0209_AvoidValueTypeBoxingInStringConcatTests
{
    // int does not box in concatenation — the compiler calls Int32.ToString directly. These
    // cases now use a struct with no ToString override, which is what actually boxes. See #50.
    [Fact]
    public async Task StringPlusBoxingStruct_Reports()
    {
        var source = """
            struct Counter { public int Value; }

            class C
            {
                void M()
                {
                    var count = new Counter();
                    var s = "Count: " {|#0:+|} count;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(0)
            .WithArguments("Counter");

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task BoxingStructPlusString_Reports()
    {
        var source = """
            struct Counter { public int Value; }

            class C
            {
                void M()
                {
                    var count = new Counter();
                    var s = count {|#0:+|} " items";
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(0)
            .WithArguments("Counter");

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    // Enums inherit System.Enum's ToString override, so the compiler calls it rather than
    // boxing. ZA0802 is the rule that belongs here, and it now fires — see #50 and #51.
    [Fact]
    public async Task StringPlusEnum_NoDiagnostic()
    {
        var source = """
            class C
            {
                enum Status { Active }

                void M()
                {
                    var s = "Status: " + Status.Active;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
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
    public async Task ChainedConcatWithTwoBoxingStructs_ReportsTwice()
    {
        var source = """
            struct Counter { public int Value; }

            class C
            {
                void M()
                {
                    var a = new Counter();
                    var b = new Counter();
                    var s = "x" {|#0:+|} a {|#1:+|} b;
                }
            }
            """;

        var expected0 = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(0)
            .WithArguments("Counter");

        var expected1 = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(1)
            .WithArguments("Counter");

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
