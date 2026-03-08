using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0008_AvoidEnumHasFlagTests
{
    [Fact]
    public async Task EnumHasFlag_Reports()
    {
        var source = """
            using System;

            [Flags]
            enum Options { None = 0, A = 1, B = 2 }

            class C
            {
                bool M(Options o) => {|#0:o.HasFlag(Options.A)|};
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidEnumHasFlagAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidEnumHasFlag)
            .WithLocation(0);

        // Only report on < net7.0 (net7.0+ HasFlag is JIT-intrinsic, no boxing)
        await CSharpAnalyzerVerifier<AvoidEnumHasFlagAnalyzer>
            .VerifyAnalyzerAsync(source, "net6.0", expected);
    }

    [Fact]
    public async Task EnumHasFlag_OnNet7_NoDiagnostic()
    {
        var source = """
            using System;

            [Flags]
            enum Options { None = 0, A = 1, B = 2 }

            class C
            {
                bool M(Options o) => o.HasFlag(Options.A);
            }
            """;

        // net7.0+ JIT inlines HasFlag — no boxing
        await CSharpAnalyzerVerifier<AvoidEnumHasFlagAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net7.0");
    }

    [Fact]
    public async Task BitwiseCheck_NoDiagnostic()
    {
        var source = """
            using System;

            [Flags]
            enum Options { None = 0, A = 1, B = 2 }

            class C
            {
                bool M(Options o) => (o & Options.A) != 0;
            }
            """;

        await CSharpAnalyzerVerifier<AvoidEnumHasFlagAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net6.0");
    }
}
