using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0602_AvoidParamsInLoopsTests
{
    [Fact]
    public async Task ParamsCallInLoop_Reports()
    {
        var source = """
            class C
            {
                static void Log(string format, params object[] args) { }

                void M()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        {|#0:Log("value: {0}", i)|};
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidParamsInLoops)
            .WithLocation(0)
            .WithArguments("Log");

        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task ParamsCallOutsideLoop_NoDiagnostic()
    {
        var source = """
            class C
            {
                static void Log(string format, params object[] args) { }

                void M()
                {
                    Log("value: {0}", 42);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task NonParamsCallInLoop_NoDiagnostic()
    {
        var source = """
            class C
            {
                static void Log(string msg) { }

                void M()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Log("hello");
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    // A params collection call in a loop, with the parameter declaration and call arguments filled in.
    private static string LoopCall(string parameter, string arguments, bool expectDiagnostic)
    {
        var call = expectDiagnostic ? "{|#0:Take(" + arguments + ")|}" : "Take(" + arguments + ")";
        return $$"""
            using System;
            using System.Collections.Generic;

            class C
            {
                static void Take({{parameter}}) { }

                void M(int[] existing)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        {{call}};
                    }
                }
            }
            """;
    }

    private static DiagnosticResult Expected() =>
        CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidParamsInLoops)
            .WithLocation(0)
            .WithArguments("Take");

    [Fact]
    public async Task ParamsReadOnlySpanInLoop_NoDiagnostic()
    {
        // Issue #70: params ReadOnlySpan<T> uses an inline array on .NET 8+, so nothing is allocated.
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(LoopCall("params ReadOnlySpan<int> ints", "1, 2, 3, 5", false), "net8.0");
    }

    [Theory]
    [InlineData("params ReadOnlySpan<int> xs", "i, 2")]
    [InlineData("params Span<int> xs", "i, 2")]
    [InlineData("params ReadOnlySpan<object> xs", "i, \"a\"")]
    public async Task ParamsSpanWithInlineArraySupport_NoDiagnostic(string parameter, string arguments)
    {
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(LoopCall(parameter, arguments, false), "net8.0");
    }

    [Theory]
    [InlineData("params ReadOnlySpan<int> xs", "i, 2")]
    [InlineData("params Span<int> xs", "1, 2")]
    [InlineData("params ReadOnlySpan<string> xs", "\"a\", \"b\"")]
    [InlineData("params ReadOnlySpan<int> xs", "1, 2")]
    public async Task ParamsSpanOnNet6_AllocatesArray_Reports(string parameter, string arguments)
    {
        // Without inline arrays the compiler falls back to new T[] for these spans. On .NET 6 that
        // includes constant ReadOnlySpan<int>, because RuntimeHelpers.CreateSpan only arrived in .NET 7.
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyAnalyzerAsync(LoopCall(parameter, arguments, true), "net6.0", ReferenceAssemblies.Net.Net60, Expected());
    }

    [Fact]
    public async Task ParamsReadOnlySpanOfConstantBytesOnNet6_NoDiagnostic()
    {
        // Constant single-byte data is emitted as a static data blob on every runtime.
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(LoopCall("params ReadOnlySpan<byte> xs", "1, 2, 3", false), "net6.0", ReferenceAssemblies.Net.Net60);
    }

    [Fact]
    public async Task ParamsReadOnlySpanOfConstantIntsOnNet7_NoDiagnostic()
    {
        // .NET 7 adds RuntimeHelpers.CreateSpan, so constant wider primitives also come from static data.
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(LoopCall("params ReadOnlySpan<int> xs", "1, 2, 3", false), "net7.0", ReferenceAssemblies.Net.Net70);
    }

    [Theory]
    [InlineData("params List<int> xs")]
    [InlineData("params IEnumerable<int> xs")]
    public async Task ParamsHeapCollectionInLoop_Reports(string parameter)
    {
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyAnalyzerAsync(LoopCall(parameter, "i, 2", true), "net8.0", Expected());
    }

    [Fact]
    public async Task ExistingArrayPassedToParamsInLoop_NoDiagnostic()
    {
        // Passing an array in normal form allocates nothing new.
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(LoopCall("params int[] xs", "existing", false), "net8.0");
    }

    [Fact]
    public async Task EmptyParamsArrayInLoop_NoDiagnostic()
    {
        // An empty params array is emitted as Array.Empty<T>().
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(LoopCall("params int[] xs", "", false), "net8.0");
    }

    [Fact]
    public async Task SingleElementParamsArrayInLoop_Reports()
    {
        await CSharpAnalyzerVerifier<AvoidParamsInLoopsAnalyzer>
            .VerifyAnalyzerAsync(LoopCall("params int[] xs", "i", true), "net8.0", Expected());
    }
}
