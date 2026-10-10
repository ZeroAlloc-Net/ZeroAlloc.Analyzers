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

    [Theory]
    [InlineData("Span<byte>", "return buffer.AsSpan();")]
    [InlineData("Span<byte>", "return buffer;")]
    [InlineData("ReadOnlySpan<byte>", "return buffer;")]
    [InlineData("Span<byte>", "return new Span<byte>(buffer);")]
    [InlineData("ReadOnlySpan<byte>", "return buffer.AsSpan(0, 4);")]
    public async Task SpanReturned_Reports(string returnType, string statement)
    {
        // A span cannot carry ownership back to the pool, so the array is never returned.
        var source = $$"""
            using System;
            using System.Buffers;

            class C
            {
                {{returnType}} M()
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    {{statement}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task SpanReturnedFromSpanReturningLocalFunction_Reports()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                int M()
                {
                    return Slice().Length;

                    static Span<byte> Slice()
                    {
                        var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                        return buffer.AsSpan(0, 4);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
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
    public async Task SpanLocalReturnedFromSpanMethod_Reports()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                Span<byte> M()
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    Span<byte> s = buffer;
                    return s;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Theory]
    [InlineData("var read = await stream.ReadAsync(buffer, 0, buffer.Length);")]
    [InlineData("var read = await stream.ReadAsync(buffer);")]
    [InlineData("var read = await stream.ReadAsync(buffer.AsMemory(0, 8));")]
    [InlineData("var read = await stream.ReadAsync(new Memory<byte>(buffer, 0, 8));")]
    [InlineData("await stream.WriteAsync(buffer, 0, 4); var read = 0;")]
    [InlineData("await stream.WriteAsync((ReadOnlyMemory<byte>)buffer); var read = 0;")]
    [InlineData("var task = stream.ReadAsync(buffer, 0, 8); var read = await task;")]
    public async Task PassedToAsyncStreamCall_Reports(string statement)
    {
        // Stream does not keep the buffer once the returned task completes.
        var source = $$"""
            using System;
            using System.Buffers;
            using System.IO;
            using System.Threading.Tasks;

            class C
            {
                async Task<int> M(Stream stream)
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    {{statement}}
                    return read;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task PassedToAsyncStreamCallAndReturnedInFinally_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;
            using System.IO;
            using System.Threading.Tasks;

            class C
            {
                async Task<int> M(Stream stream)
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    try
                    {
                        return await stream.ReadAsync(buffer.AsMemory(0, 8));
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }

    [Theory]
    [InlineData("_memory = buffer; return await stream.ReadAsync(_memory);")]
    [InlineData("_memory = buffer.AsMemory(0, 8); return await stream.ReadAsync(_memory);")]
    [InlineData("Memory<byte> memory = buffer; return await stream.ReadAsync(memory);")]
    [InlineData("var memory = buffer.AsMemory(); return await stream.ReadAsync(memory);")]
    [InlineData("return await Read(stream, buffer);")]
    public async Task MemoryStoredOrPassedToOtherAsyncCall_NoDiagnostic(string statements)
    {
        // Only a conversion passed straight into a Stream call is known not to escape.
        var source = $$"""
            using System;
            using System.Buffers;
            using System.IO;
            using System.Threading.Tasks;

            class C
            {
                private Memory<byte> _memory;

                private static Task<int> Read(Stream stream, byte[] buffer) => stream.ReadAsync(buffer, 0, 8);

                async Task<int> M(Stream stream)
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    {{statements}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }

    [Theory]
    [InlineData("stream.Keep(buffer); return 0;")]
    [InlineData("await stream.EnqueueAsync(buffer); return 0;")]
    public async Task PassedToMethodAddedByStreamSubclass_NoDiagnostic(string statements)
    {
        // Only Stream's own members and their overrides are known not to keep the array.
        var source = $$"""
            using System;
            using System.Buffers;
            using System.IO;
            using System.Threading.Tasks;

            abstract class QueueStream : Stream
            {
                public abstract void Keep(byte[] buffer);
                public abstract Task EnqueueAsync(byte[] buffer);
            }

            class C
            {
                async Task<int> M(QueueStream stream)
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    {{statements}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }

    [Fact]
    public async Task PassedToOverrideOnStreamSubclass_Reports()
    {
        var source = """
            using System;
            using System.Buffers;
            using System.IO;
            using System.Threading.Tasks;

            class C
            {
                async Task<int> M(FileStream stream)
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    stream.Write(buffer, 0, 4);
                    return await stream.ReadAsync(buffer.AsMemory(0, 8));
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Theory]
    [InlineData("Array.Copy(buffer, new byte[4], 4);")]
    [InlineData("Array.Clear(buffer);")]
    [InlineData("Array.Clear(buffer, 0, 4);")]
    [InlineData("Buffer.BlockCopy(buffer, 0, new byte[4], 0, 4);")]
    public async Task PassedToArrayOrBufferAsArray_Reports(string statement)
    {
        // These take System.Array, so the rented array reaches them through a reference conversion.
        var source = $$"""
            using System;
            using System.Buffers;

            class C
            {
                int M()
                {
                    var buffer = {|#0:ArrayPool<byte>.Shared.Rent(16)|};
                    {{statement}}
                    return buffer.Length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("buffer"));
    }

    [Fact]
    public async Task ConvertedToArrayForUntrustedCall_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Buffers;

            class C
            {
                private Array _kept;

                private void Keep(Array array) => _kept = array;

                int M()
                {
                    var buffer = ArrayPool<byte>.Shared.Rent(16);
                    Keep(buffer);
                    return buffer.Length;
                }
            }
            """;

        await CSharpAnalyzerVerifier<ReturnRentedArrayAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0");
    }
}
