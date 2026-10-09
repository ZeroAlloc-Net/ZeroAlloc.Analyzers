using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0111_AvoidConcurrentDictionarySnapshotTests
{
    private static DiagnosticResult Expected(string access, string member) =>
        CSharpAnalyzerVerifier<AvoidConcurrentDictionarySnapshotAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidConcurrentDictionarySnapshot)
            .WithLocation(0)
            .WithArguments(access, member);

    [Fact]
    public async Task ForeachOverKeys_ReportsAndFixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                void M(ConcurrentDictionary<int, string> map)
                {
                    foreach (var key in {|#0:map.Keys|})
                    {
                        System.Console.WriteLine(key);
                    }
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                void M(ConcurrentDictionary<int, string> map)
                {
                    foreach (var (key, _) in map)
                    {
                        System.Console.WriteLine(key);
                    }
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidConcurrentDictionarySnapshotAnalyzer, AvoidConcurrentDictionarySnapshotCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("map.Keys", "Key"));
    }

    [Fact]
    public async Task ForeachOverValuesWithExplicitType_ReportsAndFixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                int M(ConcurrentDictionary<int, string> map)
                {
                    var total = 0;
                    foreach (string value in {|#0:map.Values|})
                        total += value.Length;
                    return total;
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                int M(ConcurrentDictionary<int, string> map)
                {
                    var total = 0;
                    foreach (var (_, value) in map)
                        total += value.Length;
                    return total;
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidConcurrentDictionarySnapshotAnalyzer, AvoidConcurrentDictionarySnapshotCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("map.Values", "Value"));
    }

    [Fact]
    public async Task LinqOverValues_Reports()
    {
        var source = """
            using System.Collections.Concurrent;
            using System.Linq;

            class C
            {
                int M(ConcurrentDictionary<int, string> map) => {|#0:map.Values|}.Count(v => v.Length > 0);
            }
            """;

        await CSharpAnalyzerVerifier<AvoidConcurrentDictionarySnapshotAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("map.Values", "Value"));
    }

    [Theory]
    [InlineData("var snapshot = map.Keys; return snapshot.Count;")]
    [InlineData("return map.Keys.Count;")]
    [InlineData("var total = 0; foreach (var key in plain.Keys) total += key; return total;")]
    public async Task NotEnumeratingTheSnapshot_NoDiagnostic(string statements)
    {
        // A stored snapshot may be intentional, Count is not an enumeration, and Dictionary.Keys is a cheap view.
        var source = $$"""
            using System.Collections.Concurrent;
            using System.Collections.Generic;

            class C
            {
                int M(ConcurrentDictionary<int, string> map, Dictionary<int, string> plain)
                {
                    {{statements}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidConcurrentDictionarySnapshotAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
