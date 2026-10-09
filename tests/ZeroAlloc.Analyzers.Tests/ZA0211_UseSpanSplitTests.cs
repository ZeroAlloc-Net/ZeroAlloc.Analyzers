using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
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
    [InlineData("csv.Split(csv)")]
    public async Task UnsupportedSplitForms_NoDiagnostic(string collection)
    {
        // The span enumerator has no StringSplitOptions and splits on one separator. A string
        // separator must be a non-empty constant, because the two Splits treat an empty one differently.
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
    [Theory]
    [InlineData("async Task M(string csv) { foreach (var part in csv.Split(',')) await Task.Yield(); }")]
    [InlineData("async Task M(string csv) { foreach (var part in csv.Split(',')) { await using var d = new D(); } }")]
    [InlineData("IEnumerable<int> M(string csv) { foreach (var part in csv.Split(',')) yield return part.Length; }")]
    public async Task LoopBodyAwaitsOrYields_NoDiagnostic(string method)
    {
        // A ref struct enumerator cannot be live across an await or a yield.
        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyNoDiagnosticAsync(Member(method), "net9.0", ReferenceAssemblies.Net.Net90);
    }

    [Theory]
    [InlineData("async Task<int> M(string csv) { var n = 0; foreach (var part in {|#0:csv.Split(',')|}) n += part.Length; await Task.Yield(); return n; }")]
    [InlineData("IEnumerable<int> M(string csv) { var n = 0; foreach (var part in {|#0:csv.Split(',')|}) n += part.Length; yield return n; }")]
    [InlineData("void M(string csv) { foreach (var part in {|#0:csv.Split(',')|}) { Func<Task> f = async () => await Task.Yield(); } }")]
    public async Task AsyncOrIteratorWithoutAwaitInLoop_CSharp13_Reports(string method)
    {
        await CSharpAnalyzerVerifier<UseSpanSplitAnalyzer>
            .VerifyAnalyzerAsync(Member(method), "net9.0", ReferenceAssemblies.Net.Net90, Expected());
    }

    [Theory]
    [InlineData("async Task<int> M(string csv) { var n = 0; foreach (var part in csv.Split(',')) n += part.Length; await Task.Yield(); return n; }")]
    [InlineData("IEnumerable<int> M(string csv) { var n = 0; foreach (var part in csv.Split(',')) n += part.Length; yield return n; }")]
    public async Task AsyncOrIterator_BeforeCSharp13_NoDiagnostic(string method)
    {
        // Before C# 13 a ref struct local is not allowed in an async method or an iterator at all.
        var test = new CSharpAnalyzerTest<UseSpanSplitAnalyzer, DefaultVerifier>
        {
            TestCode = Member(method),
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var parseOptions = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
            return solution.WithProjectParseOptions(projectId, parseOptions.WithLanguageVersion(LanguageVersion.CSharp12));
        });

        await test.RunAsync();
    }

    private static string Member(string method) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading.Tasks;

        class D : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => default;
        }

        class C
        {
            {{method}}
        }
        """;
}
