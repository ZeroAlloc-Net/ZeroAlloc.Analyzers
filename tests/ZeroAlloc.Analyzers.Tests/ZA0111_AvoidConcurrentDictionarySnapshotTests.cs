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

    [Theory]
    [InlineData("map[key + \"x\"] = 1;")]
    [InlineData("map[key] += 1;")]
    [InlineData("map.TryAdd(key + \"x\", 1);")]
    [InlineData("map.GetOrAdd(key + \"x\", 1);")]
    [InlineData("map.AddOrUpdate(key + \"x\", 1, (k, v) => v + 1);")]
    [InlineData("map.TryUpdate(key, 2, 1);")]
    [InlineData("map.TryRemove(key, out _);")]
    [InlineData("map.Clear();")]
    [InlineData("this.map.TryAdd(key + \"x\", 1);")]
    public async Task LoopMutatesTheSameDictionary_OffersNoFix(string statement)
    {
        // Over a snapshot the loop is finite; over live enumeration it may see its own additions.
        var source = $$"""
            using System.Collections.Concurrent;

            class C
            {
                private readonly ConcurrentDictionary<string, int> map = new();

                void M()
                {
                    foreach (var key in {|#0:map.Keys|})
                        {{statement}}
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidConcurrentDictionarySnapshotAnalyzer, AvoidConcurrentDictionarySnapshotCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected("map.Keys", "Key"));
    }

    [Fact]
    public async Task LoopMutatesAnotherDictionary_ReportsAndFixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                void M(ConcurrentDictionary<string, int> map, ConcurrentDictionary<string, int> other)
                {
                    foreach (var key in {|#0:map.Keys|})
                        other[key] = 1;
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                void M(ConcurrentDictionary<string, int> map, ConcurrentDictionary<string, int> other)
                {
                    foreach (var (key, _) in map)
                        other[key] = 1;
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidConcurrentDictionarySnapshotAnalyzer, AvoidConcurrentDictionarySnapshotCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("map.Keys", "Key"));
    }

    [Theory]
    [InlineData("foreach (object value in {|#0:map.Values|}) n++;", "map.Values", "Value")]
    [InlineData("foreach (var _ in {|#0:map.Keys|}) n++;", "map.Keys", "Key")]
    public async Task LoopVariableTheFixCannotKeep_OffersNoFix(string loop, string access, string member)
    {
        var source = $$"""
            using System.Collections.Concurrent;

            class C
            {
                int M(ConcurrentDictionary<int, string> map)
                {
                    var n = 0;
                    {{loop}}
                    return n;
                }
            }
            """;

        await CSharpCodeFixVerifier<AvoidConcurrentDictionarySnapshotAnalyzer, AvoidConcurrentDictionarySnapshotCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected(access, member));
    }

    [Theory]
    [InlineData("map.Keys.Any()")]
    [InlineData("map.Values.Count()")]
    [InlineData("map.Values.LongCount()")]
    public async Task PredicateLessCountOrAny_NoDiagnostic(string expression)
    {
        // These do not enumerate the snapshot; the copy is the cost, and IsEmpty or Count avoid it.
        var source = $$"""
            using System.Collections.Concurrent;
            using System.Linq;

            class C
            {
                object M(ConcurrentDictionary<int, string> map) => {{expression}};
            }
            """;

        await CSharpAnalyzerVerifier<AvoidConcurrentDictionarySnapshotAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task AnyWithPredicate_Reports()
    {
        var source = """
            using System.Collections.Concurrent;
            using System.Linq;

            class C
            {
                bool M(ConcurrentDictionary<int, string> map) => {|#0:map.Keys|}.Any(k => k > 1);
            }
            """;

        await CSharpAnalyzerVerifier<AvoidConcurrentDictionarySnapshotAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("map.Keys", "Key"));
    }
}
