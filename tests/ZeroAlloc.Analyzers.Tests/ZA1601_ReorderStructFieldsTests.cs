using Microsoft.CodeAnalysis.Testing;
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

    // Expected sizes below were measured with Unsafe.SizeOf on .NET 8, 9 and 10 (x64).

    private static Task VerifyReportsAsync(string source, string name, int current, int optimal, string tfm = "net8.0")
        => CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>.VerifyAnalyzerAsync(
            source,
            tfm,
            CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
                .Diagnostic(DiagnosticIds.ReorderStructFields)
                .WithLocation(0)
                .WithArguments(name, current - optimal, current, optimal));

    [Fact]
    public async Task NestedSourceStruct_Reports()
    {
        // Inner is 16 bytes, aligned to 8. byte, Inner, byte => 32; Inner, byte, byte => 24.
        var source = """
            struct Inner { public long A; public int B; }

            struct {|#0:Outer|}
            {
                public byte X;
                public Inner I;
                public byte Y;
            }
            """;

        await VerifyReportsAsync(source, "Outer", 32, 24);
    }

    [Fact]
    public async Task NestedStructUsesItsDeclaredSize_Reports()
    {
        // Padded is 24 bytes as declared (its own ZA1601 is reported separately);
        // byte, Padded, byte => 40; Padded, byte, byte => 32.
        var source = """
            struct {|#1:Padded|} { public byte A; public long B; public byte C; }

            struct {|#0:Outer|}
            {
                public byte X;
                public Padded P;
                public byte Y;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>.VerifyAnalyzerAsync(
            source,
            "net8.0",
            CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
                .Diagnostic(DiagnosticIds.ReorderStructFields)
                .WithLocation(0)
                .WithArguments("Outer", 8, 40, 32),
            CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
                .Diagnostic(DiagnosticIds.ReorderStructFields)
                .WithLocation(1)
                .WithArguments("Padded", 8, 24, 16));
    }

    [Fact]
    public async Task EmptyNestedStruct_CountsAsOneByte_Reports()
    {
        // An empty struct occupies 1 byte. byte, Empty, long => 16; long, byte, Empty => 16 (no gain),
        // so add a trailing byte: byte, Empty, long, byte => 24; long, byte, Empty, byte => 16.
        var source = """
            struct Empty { }

            struct {|#0:Outer|}
            {
                public byte A;
                public Empty E;
                public long B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "Outer", 24, 16);
    }

    [Fact]
    public async Task NestedStructWithReferenceField_NoDiagnostic()
    {
        var source = """
            struct Inner { public string S; public long A; }

            struct Outer
            {
                public byte X;
                public Inner I;
                public byte Y;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task NestedStructWithExplicitLayout_NoDiagnostic()
    {
        var source = """
            using System.Runtime.InteropServices;

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            struct Inner { public long A; public byte B; }

            struct Outer
            {
                public byte X;
                public Inner I;
                public byte Y;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task NestedInlineArray_Reports()
    {
        // Buf3 is three longs: 24 bytes aligned to 8. byte, Buf3, byte => 40; Buf3, byte, byte => 32.
        var source = """
            [System.Runtime.CompilerServices.InlineArray(3)]
            struct Buf3 { private long _e; }

            struct {|#0:Outer|}
            {
                public byte A;
                public Buf3 B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "Outer", 40, 32);
    }

    [Fact]
    public async Task GuidField_Reports()
    {
        // Guid is 16 bytes aligned to 4. byte, Guid, byte => 24; Guid, byte, byte => 20.
        var source = """
            struct {|#0:WithGuid|}
            {
                public byte A;
                public System.Guid B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "WithGuid", 24, 20);
    }

    [Fact]
    public async Task TimeSpanField_Reports()
    {
        var source = """
            struct {|#0:WithTimeSpan|}
            {
                public byte A;
                public System.TimeSpan B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "WithTimeSpan", 24, 16);
    }

    [Fact]
    public async Task NullableField_Reports()
    {
        // int? is 8 bytes aligned to 4. byte, int?, byte => 16; int?, byte, byte => 12.
        var source = """
            struct {|#0:WithNullable|}
            {
                public byte A;
                public int? B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "WithNullable", 16, 12);
    }

    [Fact]
    public async Task NativeIntField_Reports()
    {
        // Sizes assume a 64-bit target, where nint is 8 bytes.
        var source = """
            struct {|#0:WithNint|}
            {
                public byte A;
                public nint B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "WithNint", 24, 16);
    }

    [Fact]
    public async Task DateTimeField_OnModernNet_Reports()
    {
        var source = """
            struct {|#0:WithDateTime|}
            {
                public byte A;
                public System.DateTime B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "WithDateTime", 24, 16);
    }

    [Fact]
    public async Task DateTimeOffsetField_OnModernNet_Reports()
    {
        // DateTimeOffset is 16 bytes aligned to 8. byte, DateTimeOffset, byte => 32; reordered => 24.
        var source = """
            struct {|#0:WithDateTimeOffset|}
            {
                public byte A;
                public System.DateTimeOffset B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "WithDateTimeOffset", 32, 24);
    }

    [Fact]
    public async Task DecimalField_OnModernNet_Reports()
    {
        // decimal is 16 bytes aligned to 8 on .NET. byte, decimal, byte => 32; reordered => 24.
        var source = """
            struct {|#0:WithDecimal|}
            {
                public byte A;
                public decimal B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "WithDecimal", 32, 24);
    }

    [Fact]
    public async Task DateTimeField_OnNetFramework_NoDiagnostic()
    {
        // DateTime is auto layout on .NET Framework, which turns the containing struct
        // into auto layout too, so declaration order does not decide the size there.
        var source = """
            struct WithDateTime
            {
                public byte A;
                public System.DateTime B;
                public byte C;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net48", ReferenceAssemblies.NetFramework.Net48.Default);
    }

    [Fact]
    public async Task PrimitiveFields_OnNetFramework_Reports()
    {
        var source = """
            struct {|#0:Padded|}
            {
                public byte A;
                public long B;
                public byte C;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>.VerifyAnalyzerAsync(
            source,
            "net48",
            ReferenceAssemblies.NetFramework.Net48.Default,
            CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
                .Diagnostic(DiagnosticIds.ReorderStructFields)
                .WithLocation(0)
                .WithArguments("Padded", 8, 24, 16));
    }

    [Fact]
    public async Task UnknownMetadataStruct_NoDiagnostic()
    {
        // A struct from a referenced assembly whose layout we don't know: bail.
        var source = """
            struct WithRange
            {
                public byte A;
                public System.Range B;
                public byte C;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task GenericTypeParameterField_NoDiagnostic()
    {
        var source = """
            struct Holder<T>
            {
                public byte A;
                public T Value;
                public long B;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task PointerAndFixedBufferFields_Reports()
    {
        // byte, int*, fixed byte[3], long => 0..1, 8..16, 16..19, 24..32 => 32;
        // reordered: int*, long, fixed byte[3], byte => 20 => 24.
        var source = """
            unsafe struct {|#0:Native|}
            {
                public byte A;
                public int* P;
                public fixed byte Buf[3];
                public long B;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>.VerifyUnsafeAnalyzerAsync(
            source,
            CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
                .Diagnostic(DiagnosticIds.ReorderStructFields)
                .WithLocation(0)
                .WithArguments("Native", 8, 32, 24));
    }

    [Fact]
    public async Task ReadOnlyRecordStruct_Reports()
    {
        var source = """
            readonly record struct {|#0:Padded|}
            {
                public readonly byte A;
                public readonly long B;
                public readonly byte C;
            }
            """;

        await VerifyReportsAsync(source, "Padded", 24, 16);
    }

    [Fact]
    public async Task PartialStructAcrossDeclarations_ReportsOnce()
    {
        var source = """
            partial struct {|#0:Split|}
            {
                public byte A;
                public long B;
            }

            partial struct Split
            {
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "Split", 24, 16);
    }

    [Fact]
    public async Task OneWastedByte_Reports()
    {
        // byte, short, byte => 0..1, 2..4, 4..5 => 6; short, byte, byte => 4.
        var source = """
            struct {|#0:Small|}
            {
                public byte A;
                public short B;
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "Small", 6, 4);
    }

    [Fact]
    public async Task AutoPropertyBackingField_Reports()
    {
        // The backing field of P is laid out where P is declared: byte, long, byte => 24.
        var source = """
            struct {|#0:WithProperty|}
            {
                public byte A;
                public long P { get; set; }
                public byte C;
            }
            """;

        await VerifyReportsAsync(source, "WithProperty", 24, 16);
    }

    [Fact]
    public async Task PositionalRecordStruct_Reports()
    {
        var source = """
            record struct {|#0:Point|}(byte A, long B, byte C);
            """;

        await VerifyReportsAsync(source, "Point", 24, 16);
    }

    [Fact]
    public async Task FieldLikeEvent_NoDiagnostic()
    {
        // The event's backing field is a delegate reference, so the runtime uses auto layout.
        var source = """
            struct WithEvent
            {
                public byte A;
                public event System.Action E;
                public long B;
                public byte C;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task PrimaryConstructorStruct_NoDiagnostic()
    {
        // A captured primary constructor parameter becomes a hidden field we cannot see.
        var source = """
            struct Captured(long seed)
            {
                public byte A;
                public long B;
                public byte C;

                public long Seed() => seed;
            }
            """;

        await CSharpAnalyzerVerifier<ReorderStructFieldsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task EventWithExplicitAccessors_Reports()
    {
        // Explicit add/remove accessors declare no backing field.
        var source = """
            struct {|#0:WithAccessors|}
            {
                public byte A;
                public long B;
                public byte C;

                public event System.Action E { add { } remove { } }
            }
            """;

        await VerifyReportsAsync(source, "WithAccessors", 24, 16);
    }

    [Fact]
    public async Task DocsNestedExample_Reports()
    {
        // The nested example in docs/rules/data-layout.md.
        var source = """
            struct Vec3 { public float X, Y, Z; }

            struct {|#0:Body|}
            {
                public byte Kind;
                public Vec3 Position;
                public long Id;
                public byte Flags;
            }
            """;

        await VerifyReportsAsync(source, "Body", 32, 24);
    }
}
