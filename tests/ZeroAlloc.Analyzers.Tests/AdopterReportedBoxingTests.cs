using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

/// <summary>
/// Regression tests for #49, #50, #51 and #52. They arrive as two pairs: each pair is one
/// expression that was reporting the wrong rule and not reporting the right one.
///
/// The ZA0209 boundary below was measured, not assumed. Allocations per operation on .NET 10
/// in Release, via GC.GetAllocatedBytesForCurrentThread:
///
///   "abc" + 123                              40 B    "abc" + 123.ToString()           40 B
///   "abc" + AttributeTargets.Class           64 B    "abc" + ...Class.ToString()      64 B
///   "abc" + (object)123                      64 B    <- 24 B more: one box on x64
///   "abc" + struct without ToString override 80 B    <- boxes
///   "abc" + struct with ToString override    32 B    <- does not box
///
/// So boxing happens exactly when the value type does not override ToString, because the
/// compiler must box to reach object.ToString. Primitives and enums all override it.
/// </summary>
public class AdopterReportedBoxingTests
{
    // ---- #50: ZA0209 false positives -------------------------------------------------

    [Fact]
    public async Task StringPlusInt_DoesNotReportZA0209()
    {
        var source = """
            class C
            {
                void M()
                {
                    var s2 = "abc" + 123;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }

    [Fact]
    public async Task StringPlusEnum_DoesNotReportZA0209()
    {
        var source = """
            using System;

            class C
            {
                void M()
                {
                    var s3 = "abc" + AttributeTargets.Class;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }

    // ---- ZA0209 must still catch what genuinely boxes --------------------------------

    [Fact]
    public async Task StringPlusStructWithoutToStringOverride_ReportsZA0209()
    {
        var source = """
            struct Point { public int X; public int Y; }

            class C
            {
                void M()
                {
                    var p = new Point();
                    var s = "at " {|#0:+|} p;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidValueTypeBoxingInStringConcat)
            .WithLocation(0)
            .WithArguments("Point");

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringPlusStructWithToStringOverride_DoesNotReportZA0209()
    {
        var source = """
            struct Point
            {
                public int X;
                public override string ToString() => "p";
            }

            class C
            {
                void M()
                {
                    var p = new Point();
                    var s = "at " + p;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidValueTypeBoxingInStringConcatAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }

    // ---- #51: ZA0802 false negative on implicit ToString ------------------------------

    [Fact]
    public async Task StringPlusEnum_ReportsZA0802()
    {
        var source = """
            using System;

            class C
            {
                void M()
                {
                    var s3 = "abc" {|#0:+|} AttributeTargets.Class;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidEnumToStringAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidEnumToString)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidEnumToStringAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringPlusExplicitEnumToString_ReportsZA0802Once()
    {
        // The explicit form already reported. Adding the AddExpression path must not make it
        // report twice: the right operand is a string by then, so only the invocation matches.
        var source = """
            using System;

            class C
            {
                void M()
                {
                    var s = "abc" + AttributeTargets.Class.{|#0:ToString|}();
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidEnumToStringAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidEnumToString)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidEnumToStringAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    // ---- #52: ZA0802 false negative on a type parameter -------------------------------

    [Fact]
    public async Task TypeParameterConstrainedToEnum_ToString_ReportsZA0802()
    {
        var source = """
            using System;

            struct C<T> where T : struct, Enum
            {
                T _t;
                public override readonly string ToString() => _t.{|#0:ToString|}();
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidEnumToStringAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidEnumToString)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidEnumToStringAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task TypeParameterWithoutEnumConstraint_ToString_DoesNotReportZA0802()
    {
        var source = """
            struct C<T> where T : struct
            {
                T _t;
                public override readonly string ToString() => _t.ToString();
            }
            """;

        await CSharpAnalyzerVerifier<AvoidEnumToStringAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }

    // ---- #49: ZA0504 false positive on a by-value extension receiver ------------------

    [Fact]
    public async Task ExtensionMethodWithByValueThis_DoesNotReportZA0504()
    {
        // The reduced form and the static form compile to identical IL, so diagnosing one and
        // not the other cannot be right. The static form already reports nothing.
        var source = """
            using System;

            static class EnumExtensions
            {
                public static string ToStringFast<T>(this T value) where T : struct, Enum => "foo";
            }

            struct C<T> where T : struct, Enum
            {
                T _t;
                public readonly string M() => _t.ToStringFast();
            }
            """;

        await CSharpAnalyzerVerifier<AvoidDefensiveCopyAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }

    // The genuine defensive-copy cases stay covered by ZA0504_AvoidDefensiveCopyTests:
    // a readonly field or `in` parameter calling a non-readonly member still reports.
}
