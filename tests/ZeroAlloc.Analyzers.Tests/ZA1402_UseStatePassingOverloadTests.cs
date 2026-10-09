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
}
