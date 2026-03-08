using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0006_UseStackallocTests
{
    [Fact]
    public async Task SmallByteArrayInLocal_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    var buffer = {|#0:new byte[128]|};
                    _ = buffer;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<UseStackallocAnalyzer>
            .Diagnostic(DiagnosticIds.UseStackalloc)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<UseStackallocAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LargeByteArray_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var buffer = new byte[512];
                    _ = buffer;
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseStackallocAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task SmallCharArrayInLocal_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    var buffer = {|#0:new char[64]|};
                    _ = buffer;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<UseStackallocAnalyzer>
            .Diagnostic(DiagnosticIds.UseStackalloc)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<UseStackallocAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task ByteArrayAsField_NoDiagnostic()
    {
        var source = """
            class C
            {
                private byte[] _buffer = new byte[128];
            }
            """;

        await CSharpAnalyzerVerifier<UseStackallocAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task IntArrayInLocal_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var data = new int[64];
                    _ = data;
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseStackallocAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }
}
