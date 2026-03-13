using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1104_AvoidSpanInAsyncMethodTests
{
    [Fact]
    public async Task SpanParameter_InAsyncMethod_Reports()
    {
        // CS4012: C# compiler also rejects Span<T> parameters in async methods
        var source = """
            using System;
            using System.Threading.Tasks;

            class C
            {
                async Task M({|#0:Span<byte>|} data)
                {
                    await Task.Delay(1);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidSpanInAsyncMethod)
            .WithLocation(0)
            .WithArguments("data");

        // CS4012 is reported on the identifier, not the type span
        var cs4012 = DiagnosticResult.CompilerError("CS4012")
            .WithSpan(6, 29, 6, 33)
            .WithArguments("System.Span<byte>");

        await CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected, cs4012);
    }

    [Fact]
    public async Task ReadOnlySpanParameter_InAsyncMethod_Reports()
    {
        // CS4012: C# compiler also rejects ReadOnlySpan<T> parameters in async methods
        var source = """
            using System;
            using System.Threading.Tasks;

            class C
            {
                async Task M({|#0:ReadOnlySpan<byte>|} data)
                {
                    await Task.Delay(1);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidSpanInAsyncMethod)
            .WithLocation(0)
            .WithArguments("data");

        // CS4012 is reported on the identifier, not the type span
        var cs4012 = DiagnosticResult.CompilerError("CS4012")
            .WithSpan(6, 37, 6, 41)
            .WithArguments("System.ReadOnlySpan<byte>");

        await CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected, cs4012);
    }

    [Fact]
    public async Task SpanLocalVariable_InAsyncMethod_Reports()
    {
        var source = """
            using System;
            using System.Threading.Tasks;

            class C
            {
                async Task M(byte[] bytes)
                {
                    {|#0:Span<byte>|} data = bytes;
                    await Task.Delay(1);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidSpanInAsyncMethod)
            .WithLocation(0)
            .WithArguments("data");

        await CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task SpanParameter_NonAsyncMethod_NoDiagnostic()
    {
        var source = """
            using System;

            class C
            {
                void M(Span<byte> data)
                {
                    // no await
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task MemoryParameter_InAsyncMethod_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Threading.Tasks;

            class C
            {
                async Task M(Memory<byte> data)
                {
                    await Task.Delay(1);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task AsyncMethod_NoAwait_NoDiagnostic()
    {
        // non-async Task-returning method — async keyword guard prevents false positive
        var source = """
            using System;
            using System.Threading.Tasks;

            class C
            {
                Task M(Span<byte> data)
                {
                    return Task.CompletedTask;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task SpanLocalInNestedNonAsyncLocalFunction_NoDiagnostic()
    {
        // A Span<T> local inside a nested synchronous local function should NOT be flagged —
        // the local function has no await boundary of its own.
        var source = """
            using System;
            using System.Threading.Tasks;

            class C
            {
                async Task M()
                {
                    await Task.Delay(1);

                    void Inner()
                    {
                        Span<byte> s = stackalloc byte[8];
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidSpanInAsyncMethodAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
