using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1601_ReorderStructFieldsTests
{
    [Fact]
    public async Task PaddedFieldOrder_Reports()
    {
        // byte, long, byte => 24 bytes; reordered (long, byte, byte) => 16 bytes.
        var source = """
            struct {|#0:Padded|}
            {
                public byte A;
                public long B;
                public byte C;
            }
            """;

        var expected = CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .Diagnostic(DiagnosticIds.ReorderStructFields)
            .WithLocation(0)
            .WithArguments("Padded", 8, 24, 16);

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task OptimalFieldOrder_NoDiagnostic()
    {
        // long, int, int packs with no padding => 16 bytes.
        var source = """
            struct Packed
            {
                public long A;
                public int B;
                public int C;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task SingleField_NoDiagnostic()
    {
        var source = """
            struct OneField
            {
                public byte A;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ReferenceTypeField_NoDiagnostic()
    {
        // Contains a managed reference => runtime uses auto layout; we bail.
        var source = """
            struct WithRef
            {
                public byte A;
                public long B;
                public string Name;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ExplicitStructLayout_NoDiagnostic()
    {
        // Explicit [StructLayout] signals intentional ordering — never suggest reordering.
        var source = """
            using System.Runtime.InteropServices;

            [StructLayout(LayoutKind.Sequential)]
            struct Interop
            {
                public byte A;
                public long B;
                public byte C;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task EnumFieldsTreatedAsUnderlying_Reports()
    {
        // ByteFlag(byte), long, ByteFlag(byte) behaves like byte, long, byte.
        var source = """
            enum ByteFlag : byte { None, Set }

            struct {|#0:PaddedEnum|}
            {
                public ByteFlag A;
                public long B;
                public ByteFlag C;
            }
            """;

        var expected = CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .Diagnostic(DiagnosticIds.ReorderStructFields)
            .WithLocation(0)
            .WithArguments("PaddedEnum", 8, 24, 16);

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }
}
