using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0109_AvoidZeroLengthArrayAllocationTests
{
    [Fact]
    public async Task NewIntArrayZero_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = {|#0:new int[0]|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidZeroLengthArrayAllocationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidZeroLengthArrayAllocation)
            .WithLocation(0)
            .WithArguments("int");

        await CSharpAnalyzerVerifier<AvoidZeroLengthArrayAllocationAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task NewStringArrayEmptyInitializer_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = {|#0:new string[] { }|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidZeroLengthArrayAllocationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidZeroLengthArrayAllocation)
            .WithLocation(0)
            .WithArguments("string");

        await CSharpAnalyzerVerifier<AvoidZeroLengthArrayAllocationAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task NewIntArrayNonZero_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = new int[3];
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidZeroLengthArrayAllocationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task NewIntArrayWithElements_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = new int[] { 1, 2, 3 };
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidZeroLengthArrayAllocationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ArrayEmpty_NoDiagnostic()
    {
        var source = """
            using System;

            class C
            {
                void M()
                {
                    var arr = Array.Empty<int>();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidZeroLengthArrayAllocationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task NewIntArrayZero_CodeFix_ReplacesWithArrayEmpty()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = {|#0:new int[0]|};
                }
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                void M()
                {
                    var arr = Array.Empty<int>();
                }
            }
            """;

        var expected1 = CSharpCodeFixVerifier<AvoidZeroLengthArrayAllocationAnalyzer, AvoidZeroLengthArrayAllocationCodeFixProvider>
            .Diagnostic(DiagnosticIds.AvoidZeroLengthArrayAllocation)
            .WithLocation(0)
            .WithArguments("int");

        await CSharpCodeFixVerifier<AvoidZeroLengthArrayAllocationAnalyzer, AvoidZeroLengthArrayAllocationCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, expected1);
    }

    [Fact]
    public async Task NewStringArrayEmpty_CodeFix_ReplacesWithArrayEmpty()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = {|#0:new string[] { }|};
                }
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                void M()
                {
                    var arr = Array.Empty<string>();
                }
            }
            """;

        var expected2 = CSharpCodeFixVerifier<AvoidZeroLengthArrayAllocationAnalyzer, AvoidZeroLengthArrayAllocationCodeFixProvider>
            .Diagnostic(DiagnosticIds.AvoidZeroLengthArrayAllocation)
            .WithLocation(0)
            .WithArguments("string");

        await CSharpCodeFixVerifier<AvoidZeroLengthArrayAllocationAnalyzer, AvoidZeroLengthArrayAllocationCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, expected2);
    }

    [Fact]
    public async Task MultiDimensionalArrayZero_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var arr = new int[0, 0];
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidZeroLengthArrayAllocationAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task NewIntArrayZero_CodeFix_WhenSystemUsingExists_NoExtraUsing()
    {
        var source = """
            using System;

            class C
            {
                void M()
                {
                    var arr = {|#0:new int[0]|};
                }
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                void M()
                {
                    var arr = Array.Empty<int>();
                }
            }
            """;

        var expected = CSharpCodeFixVerifier<AvoidZeroLengthArrayAllocationAnalyzer, AvoidZeroLengthArrayAllocationCodeFixProvider>
            .Diagnostic(DiagnosticIds.AvoidZeroLengthArrayAllocation)
            .WithLocation(0)
            .WithArguments("int");

        await CSharpCodeFixVerifier<AvoidZeroLengthArrayAllocationAnalyzer, AvoidZeroLengthArrayAllocationCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, expected);
    }
}
