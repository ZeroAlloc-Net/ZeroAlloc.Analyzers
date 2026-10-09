using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1402_UseStatePassingOverloadTests
{
    private static DiagnosticResult Expected(string api, string captured, int location = 0) =>
        CSharpAnalyzerVerifier<UseStatePassingOverloadAnalyzer>
            .Diagnostic(DiagnosticIds.UseStatePassingOverload)
            .WithLocation(location)
            .WithArguments(api, captured);

    [Fact]
    public async Task GetOrAddCapturingParameter_ReportsAndFixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, {|#0:k => prefix + k|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, static (k, prefix) => prefix + k, prefix);
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("ConcurrentDictionary.GetOrAdd", "'prefix'"));
    }

    [Fact]
    public async Task QueueUserWorkItemCapturingLocal_ReportsAndFixes()
    {
        var source = """
            using System.Threading;

            class Work { public void Run() { } }

            class C
            {
                void M()
                {
                    var work = new Work();
                    ThreadPool.QueueUserWorkItem({|#0:_ => work.Run()|});
                }
            }
            """;

        var fixedSource = """
            using System.Threading;

            class Work { public void Run() { } }

            class C
            {
                void M()
                {
                    var work = new Work();
                    ThreadPool.QueueUserWorkItem(static work => work.Run(), work, preferLocal: false);
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("ThreadPool.QueueUserWorkItem", "'work'"));
    }

    [Theory]
    [InlineData("var value = cache.GetOrAdd(1, {|#0:k => prefix + k|}); prefix = \"changed\"; return value;")]
    [InlineData("return cache.GetOrAdd(1, {|#0:(int k) => prefix + k|});")]
    public async Task GetOrAddFixWouldChangeMeaningOrNeedTypes_ReportsWithoutFix(string body)
    {
        // A later write is seen by a closure but not by a state snapshot; a typed lambda would need the state's type spelled out.
        var source = $$"""
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    {{body}}
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected("ConcurrentDictionary.GetOrAdd", "'prefix'"));
    }

    [Fact]
    public async Task QueueUserWorkItemUsingItsParameter_ReportsWithoutFix()
    {
        var source = """
            using System.Threading;

            class Work { public void Run(object state) { } }

            class C
            {
                void M(Work work)
                {
                    ThreadPool.QueueUserWorkItem({|#0:state => work.Run(state)|});
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected("ThreadPool.QueueUserWorkItem", "'work'"));
    }

    [Theory]
    [InlineData("token.Register({|#0:() => other.Cancel()|});", "CancellationToken.Register", "'other'")]
    [InlineData("Task.Factory.StartNew({|#0:() => other.Cancel()|});", "TaskFactory.StartNew", "'other'")]
    [InlineData("_ = string.Create(3, 'x', {|#0:(span, c) => span.Fill(fill)|});", "String.Create", "'fill'")]
    [InlineData("cache.GetOrAdd(1, {|#0:k => (k + _offset + fill).ToString()|});", "ConcurrentDictionary.GetOrAdd", "'this', 'fill'")]
    public async Task OtherApisAndCaptures_Report(string statement, string api, string captured)
    {
        // Object-state APIs, string.Create's existing state, and captures of 'this' or several
        // variables get the diagnostic but no fix.
        var source = $$"""
            using System;
            using System.Collections.Concurrent;
            using System.Threading;
            using System.Threading.Tasks;

            class C
            {
                private int _offset;

                void M(CancellationToken token, CancellationTokenSource other, ConcurrentDictionary<int, string> cache, char fill)
                {
                    {{statement}}
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected(api, captured));
    }

    [Fact]
    public async Task AddOrUpdateWithTwoCapturingLambdas_ReportsBoth()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.AddOrUpdate(1, {|#0:k => prefix|}, {|#1:(k, old) => old + prefix|});
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseStatePassingOverloadAnalyzer>.VerifyAnalyzerAsync(
            source,
            "net8.0",
            Expected("ConcurrentDictionary.AddOrUpdate", "'prefix'", 0),
            Expected("ConcurrentDictionary.AddOrUpdate", "'prefix'", 1));
    }

    [Theory]
    [InlineData("return cache.GetOrAdd(1, static k => k.ToString());")]
    [InlineData("return cache.GetOrAdd(1, k => k.ToString());")]
    [InlineData("return cache.GetOrAdd(1, static (k, p) => p + k, prefix);")]
    [InlineData("return cache.GetOrAdd(1, Make);")]
    [InlineData("return Task.Run(() => prefix).Result;")]
    public async Task NothingCapturedOrApiNotListed_NoDiagnostic(string body)
    {
        // Static or non-capturing lambdas, the state overload already in use, method groups,
        // and APIs with no state-passing overload.
        var source = $$"""
            using System.Collections.Concurrent;
            using System.Threading.Tasks;

            class C
            {
                static string Make(int k) => k.ToString();

                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    {{body}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseStatePassingOverloadAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    private static Task VerifyFix(string source, string fixedSource, string api, string captured) =>
        CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected(api, captured));

    [Fact]
    public async Task AsyncLambda_KeepsAsyncModifier()
    {
        var source = """
            using System.Collections.Concurrent;
            using System.Threading.Tasks;

            class C
            {
                static Task<string> Load(int k, string p) => Task.FromResult(p + k);

                Task<string> M(ConcurrentDictionary<int, Task<string>> cache, string prefix)
                {
                    return cache.GetOrAdd(1, {|#0:async k => await Load(k, prefix)|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;
            using System.Threading.Tasks;

            class C
            {
                static Task<string> Load(int k, string p) => Task.FromResult(p + k);

                Task<string> M(ConcurrentDictionary<int, Task<string>> cache, string prefix)
                {
                    return cache.GetOrAdd(1, static async (k, prefix) => await Load(k, prefix), prefix);
                }
            }
            """;

        await VerifyFix(source, fixedSource, "ConcurrentDictionary.GetOrAdd", "'prefix'");
    }

    [Fact]
    public async Task CSharp8_ReportsWithoutFix()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, {|#0:k => prefix + k|});
                }
            }
            """;

        await CSharpCodeFixVerifier<UseStatePassingOverloadAnalyzer, UseStatePassingOverloadCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                source,
                Expected("ConcurrentDictionary.GetOrAdd", "'prefix'"),
                languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp8);
    }

    [Fact]
    public async Task GetOrAddWithNamedArguments_NamesTheStateArgument()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(valueFactory: {|#0:k => prefix + k|}, key: 1);
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(valueFactory: static (k, prefix) => prefix + k, key: 1, factoryArgument: prefix);
                }
            }
            """;

        await VerifyFix(source, fixedSource, "ConcurrentDictionary.GetOrAdd", "'prefix'");
    }

    [Fact]
    public async Task QueueUserWorkItemWithNamedArgument_NamesTheStateArgument()
    {
        var source = """
            using System.Threading;

            class Work { public void Run() { } }

            class C
            {
                void M()
                {
                    var work = new Work();
                    ThreadPool.QueueUserWorkItem(callBack: {|#0:_ => work.Run()|});
                }
            }
            """;

        var fixedSource = """
            using System.Threading;

            class Work { public void Run() { } }

            class C
            {
                void M()
                {
                    var work = new Work();
                    ThreadPool.QueueUserWorkItem(callBack: static work => work.Run(), state: work, preferLocal: false);
                }
            }
            """;

        await VerifyFix(source, fixedSource, "ThreadPool.QueueUserWorkItem", "'work'");
    }

    [Theory]
    [InlineData("string Helper(int x) => prefix + x; return cache.GetOrAdd(1, {|#0:k => prefix + Helper(k)|});")]
    [InlineData("string Helper(int x) => prefix + x; return cache.GetOrAdd(1, {|#0:k => Apply(Helper, k) + prefix|});")]
    public async Task CapturingLocalFunction_ReportsWithoutFix(string body)
    {
        var source = $$"""
            using System;
            using System.Collections.Concurrent;

            class C
            {
                static string Apply(Func<int, string> f, int k) => f(k);

                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    {{body}}
                }
            }
            """;

        await VerifyFix(source, source, "ConcurrentDictionary.GetOrAdd", "'prefix'");
    }

    [Theory]
    [InlineData("var counter = new Counter(); return cache.GetOrAdd(1, {|#0:k => { counter.N++; return k.ToString(); }|});", "'counter'")]
    [InlineData("var counter = new Counter(); var v = cache.GetOrAdd(1, {|#0:k => counter.N.ToString()|}); counter.N = 5; return v;", "'counter'")]
    [InlineData("int s = 0; var v = cache.GetOrAdd(1, {|#0:k => (s + k).ToString()|}); ref var r = ref s; r = 5; return v;", "'s'")]
    [InlineData("string a = \"\", b = \"\"; var v = cache.GetOrAdd(1, {|#0:k => prefix + k|}); (a, (prefix, b)) = (\"x\", (\"y\", \"z\")); return v + a + b;", "'prefix'")]
    public async Task MutableStructRefAliasOrNestedDeconstruction_ReportsWithoutFix(string body, string captured)
    {
        var source = $$"""
            using System.Collections.Concurrent;

            struct Counter { public int N; }

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    {{body}}
                }
            }
            """;

        await VerifyFix(source, source, "ConcurrentDictionary.GetOrAdd", captured);
    }

    [Fact]
    public async Task PrimaryConstructorParameter_CountsAsThis()
    {
        var source = """
            using System.Collections.Concurrent;

            class C(string s)
            {
                string M(ConcurrentDictionary<int, string> cache)
                {
                    return cache.GetOrAdd(1, {|#0:k => s + k|});
                }
            }
            """;

        await VerifyFix(source, source, "ConcurrentDictionary.GetOrAdd", "'this'");
    }

    [Fact]
    public async Task PrimaryConstructorParameterInFieldInitializer_IsAParameterCapture()
    {
        var source = """
            using System.Collections.Concurrent;

            class C(string s)
            {
                static readonly ConcurrentDictionary<int, string> Cache = new();

                string _v = Cache.GetOrAdd(1, {|#0:k => s + k|});
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C(string s)
            {
                static readonly ConcurrentDictionary<int, string> Cache = new();

                string _v = Cache.GetOrAdd(1, static (k, s) => s + k, s);
            }
            """;

        await VerifyFix(source, fixedSource, "ConcurrentDictionary.GetOrAdd", "'s'");
    }

    [Fact]
    public async Task ConstLocalCaptured_NoDiagnostic()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache)
                {
                    const string P = "x";
                    return cache.GetOrAdd(1, k => P + k);
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseStatePassingOverloadAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LambdaStoredAsDictionaryValue_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Collections.Concurrent;
            using System.Threading;

            class C
            {
                Action M(ConcurrentDictionary<string, Action> cache, string name, CancellationTokenSource other)
                {
                    return cache.GetOrAdd(name, () => other.Cancel());
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseStatePassingOverloadAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task MultiLineArgumentList_KeepsLineBreaksAndComments()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(
                        1, // the key
                        {|#0:k => prefix + k|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(
                        1, // the key
                        static (k, prefix) => prefix + k,
                        prefix);
                }
            }
            """;

        await VerifyFix(source, fixedSource, "ConcurrentDictionary.GetOrAdd", "'prefix'");
    }

    [Fact]
    public async Task UnsafeQueueUserWorkItem_ReportsWithoutFix()
    {
        var source = """
            using System.Threading;

            class Work { public void Run() { } }

            class C
            {
                void M()
                {
                    var work = new Work();
                    ThreadPool.UnsafeQueueUserWorkItem({|#0:_ => work.Run()|}, null);
                }
            }
            """;

        await VerifyFix(source, source, "ThreadPool.UnsafeQueueUserWorkItem", "'work'");
    }

    [Fact]
    public async Task BlockBodiedLambda_Fixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, {|#0:k =>
                    {
                        return prefix + k;
                    }|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, static (k, prefix) =>
                    {
                        return prefix + k;
                    }, prefix);
                }
            }
            """;

        await VerifyFix(source, fixedSource, "ConcurrentDictionary.GetOrAdd", "'prefix'");
    }

    [Fact]
    public async Task ParenthesizedLambda_Fixes()
    {
        var source = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, {|#0:(k) => prefix + k|});
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;

            class C
            {
                string M(ConcurrentDictionary<int, string> cache, string prefix)
                {
                    return cache.GetOrAdd(1, static (k, prefix) => prefix + k, prefix);
                }
            }
            """;

        await VerifyFix(source, fixedSource, "ConcurrentDictionary.GetOrAdd", "'prefix'");
    }

    [Fact]
    public async Task ForeachIterationVariable_Fixes()
    {
        var source = """
            using System.Collections.Concurrent;
            using System.Collections.Generic;

            class C
            {
                void M(ConcurrentDictionary<int, string> cache, List<string> items)
                {
                    foreach (var item in items)
                    {
                        cache.GetOrAdd(1, {|#0:k => item + k|});
                    }
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Concurrent;
            using System.Collections.Generic;

            class C
            {
                void M(ConcurrentDictionary<int, string> cache, List<string> items)
                {
                    foreach (var item in items)
                    {
                        cache.GetOrAdd(1, static (k, item) => item + k, item);
                    }
                }
            }
            """;

        await VerifyFix(source, fixedSource, "ConcurrentDictionary.GetOrAdd", "'item'");
    }

    [Fact]
    public async Task PrimaryConstructorParameterInEventFieldInitializer_IsAParameterCapture()
    {
        var source = """
            using System;
            using System.Collections.Concurrent;

            class C(string s)
            {
                static readonly ConcurrentDictionary<int, string> Cache = new();

                public event Func<string> E = () => Cache.GetOrAdd(1, {|#0:k => s + k|});
            }
            """;

        var fixedSource = """
            using System;
            using System.Collections.Concurrent;

            class C(string s)
            {
                static readonly ConcurrentDictionary<int, string> Cache = new();

                public event Func<string> E = () => Cache.GetOrAdd(1, static (k, s) => s + k, s);
            }
            """;

        await VerifyFix(source, fixedSource, "ConcurrentDictionary.GetOrAdd", "'s'");
    }
}
