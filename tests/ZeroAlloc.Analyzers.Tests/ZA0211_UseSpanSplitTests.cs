using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0211_UseSpanSplitTests
{
    private static string Loop(string collection) => $$"""
        using System;

        class C
        {
            int M(string csv)
            {
                var count = 0;
                foreach (var part in {{collection}})
                    count += part.Length;
                return count;
            }
        }
        """;

    private static DiagnosticResult Expected() =>
        CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .Diagnostic(DiagnosticIds.UseSpanSplit)
            .WithLocation(0)
            .WithArguments("csv");

    [Theory]
    [InlineData("{|#0:csv.Split(',')|}")]
    [InlineData("{|#0:csv.Split(\", \")|}")]
    [InlineData("{|#0:csv.Split(',', StringSplitOptions.None)|}")]
    public async Task ForeachOverSplit_OnNet9_Reports(string collection)
    {
        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyAnalyzerAsync(Loop(collection), "net9.0", ReferenceAssemblies.Net.Net90, Expected());
    }

    [Theory]
    [InlineData("csv.Split(',', StringSplitOptions.RemoveEmptyEntries)")]
    [InlineData("csv.Split(',', ';')")]
    [InlineData("csv.Split(\"\")")]
    public async Task UnsupportedSplitForms_NoDiagnostic(string collection)
    {
        // The span enumerator has no StringSplitOptions and splits on one separator.
        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyNoDiagnosticAsync(Loop(collection), "net9.0", ReferenceAssemblies.Net.Net90);
    }

    [Fact]
    public async Task ForeachOverSplit_OnNet8_NoDiagnostic()
    {
        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyNoDiagnosticAsync(Loop("csv.Split(',')"), "net8.0");
    }

    [Fact]
    public async Task SplitResultKept_NoDiagnostic()
    {
        var source = """
            class C
            {
                int M(string csv)
                {
                    var parts = csv.Split(',');
                    return parts.Length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net9.0", ReferenceAssemblies.Net.Net90);
    }
}
