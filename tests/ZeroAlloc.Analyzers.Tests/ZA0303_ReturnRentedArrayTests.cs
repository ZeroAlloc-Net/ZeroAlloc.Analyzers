using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0303_ReturnRentedArrayTests
{
    private static DiagnosticResult Expected(string local) =>
        CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .Diagnostic(DiagnosticIds.ReturnRentedArray)
            .WithLocation(0)
            .WithArguments(local);

    [Fact]
    public async Task RentedAndNeverReturned_Reports()
    {
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    buffer[0] = 1;
                    return buffer.Length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task RentedIntoExistingLocal_Reports()
    {
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    byte[] buffer;
                    buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    return buffer.Length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task PassedOnlyToSpanAndStreamApis_Reports()
    {
        // MemoryExtensions and Stream read or write the array without taking ownership.
        var source = """
            using System;
            using System.Buffers;
            using System.IO;

            class C
            {
                int M(Stream stream)
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    var read = stream.Read(buffer, 0, buffer.Length);
                    buffer.AsSpan(0, read).Clear();
                    return read;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task ReturnedInFinally_NoDiagnostic()
    {
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    try
                    {
                        return buffer.Length;
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ReturnedOutsideFinally_NoDiagnostic()
    {
        // Letting the GC take the buffer on the exception path is accepted .NET guidance.
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    var length = buffer.Length;
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                    return length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Theory]
    [InlineData("return buffer;")]
    [InlineData("_field = buffer; return null;")]
    [InlineData("Keep(buffer); return null;")]
    [InlineData("var alias = buffer; return alias.Length;")]
    [InlineData("Func<int> length = () => buffer.Length; return length();")]
    public async Task ArrayEscapes_NoDiagnostic(string statements)
    {
        // Once the array leaves the method, ownership may have moved, so the rule stays silent.
        var source = $$"""
            using System;
            using System.Buffers;

            class C
            {
                private byte[] _field = Array.Empty<byte>();

                private static void Keep(byte[] array) { }

                object M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    {{statements}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task RentNotStoredInLocal_NoDiagnostic()
    {
        var source = """
            using System.Buffers;

            class C
            {
                private static void Keep(byte[] array) { }

                void M() => Keep(ArrayPool<byte>.Shared.Rent(16));
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ForeachOverRentedArrayWithoutReturn_Reports()
    {
        var source = """
            using System.Buffers;

            class C
            {
                int M()
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    var sum = 0;
                    foreach (var b in buffer)
                    {
                        sum += b;
                    }
                    return sum;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task RentAndReturnInsideLambda_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                void M()
                {
                    Action a = () =>
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent(16);
                        ArrayPool<byte>.Shared.Return(buffer);
                    };
                    a();
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ArrayReturnedAsMemory_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                Memory<byte> M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    return buffer;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ArrayStoredAsMemoryField_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                private Memory<byte> _mem;

                void M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    _mem = buffer;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ArrayPassedToMemoryParameter_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                private static void Keep(Memory<byte> memory) { }

                void M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    Keep(buffer);
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task MemoryFromAsMemoryReturned_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                Memory<byte> M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    return buffer.AsMemory(0, 4);
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task SpanFromAsSpanReturned_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                Span<byte> M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    return buffer.AsSpan();
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task SpanLocalWithoutReturn_Reports()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                void M()
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    var span = buffer.AsSpan();
                    span.Clear();
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task SpanLocalReturnedFromSpanMethod_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                Span<byte> M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    var s = buffer.AsSpan();
                    return s;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
