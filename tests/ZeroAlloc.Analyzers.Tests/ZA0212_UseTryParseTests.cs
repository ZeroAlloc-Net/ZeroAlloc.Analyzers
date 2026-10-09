using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0212_UseTryParseTests
{
    private static DiagnosticResult Expected(string type) =>
        CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .Diagnostic(DiagnosticIds.UseTryParse)
            .WithLocation(0)
            .WithArguments(type);

    private static string TryCatch(string parse, string catchClause) => $$"""
        using System;

        class Parsable
        {
            public static Parsable Parse(string s) => new();
            public static bool TryParse(string s, out Parsable result) { result = new(); return true; }
        }

        class NoTryParse
        {
            public static NoTryParse Parse(string s) => new();
        }

        class C
        {
            object M(string s)
            {
                try
                {
                    return {{parse}};
                }
                {{catchClause}}
            }
        }
        """;

    [Theory]
    [InlineData("catch (FormatException) { return 0; }")]
    [InlineData("catch (OverflowException) { return 0; }")]
    [InlineData("catch (ArgumentException) { return 0; }")]
    [InlineData("catch (Exception) { return 0; }")]
    [InlineData("catch { return 0; }")]
    public async Task IntParseCaughtAndSwallowed_Reports(string catchClause)
    {
        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyAnalyzerAsync(TryCatch("{|#0:int.Parse(s)|}", catchClause), "net8.0", Expected("int"));
    }

    [Theory]
    [InlineData("{|#0:Enum.Parse<DayOfWeek>(s)|}", "Enum")]
    [InlineData("{|#0:Parsable.Parse(s)|}", "Parsable")]
    public async Task OtherParsableTypes_Report(string parse, string type)
    {
        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyAnalyzerAsync(TryCatch(parse, "catch (Exception) { return 0; }"), "net8.0", Expected(type));
    }

    [Theory]
    [InlineData("int.Parse(s)", "catch (FormatException) { throw; }")]
    [InlineData("int.Parse(s)", "catch (FormatException e) { throw new InvalidOperationException(\"bad\", e); }")]
    [InlineData("int.Parse(s)", "catch (InvalidOperationException) { return 0; }")]
    [InlineData("int.Parse(s)", "catch (FormatException) when (s.Length > 3) { return 0; }")]
    [InlineData("NoTryParse.Parse(s)", "catch (Exception) { return 0; }")]
    public async Task NotSwallowedOrNoTryParse_NoDiagnostic(string parse, string catchClause)
    {
        // Rethrowing, an unrelated catch, a filtered catch, or a type without TryParse.
        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyNoDiagnosticAsync(TryCatch(parse, catchClause), "net8.0");
    }

    [Fact]
    public async Task ParseOutsideTry_NoDiagnostic()
    {
        var source = """
            class C
            {
                int M(string s) => int.Parse(s);
            }
            """;

        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ParseInsideCatchBlock_NoDiagnostic()
    {
        var source = """
            using System;

            class C
            {
                int M(string s, string fallback)
                {
                    try
                    {
                        return s.Length;
                    }
                    catch (Exception)
                    {
                        return int.Parse(fallback);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseTryParseAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
