using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0007_UseArrayPoolTests
{
    [Fact]
    public async Task LargeByteArrayInLocal_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    var buffer = {|#0:new byte[1024]|};
                    _ = buffer;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<UseArrayPoolAnalyzer>
            .Diagnostic(DiagnosticIds.UseArrayPool)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<UseArrayPoolAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task VariableSizeByteArrayInLocal_Reports()
    {
        var source = """
            class C
            {
                void M(int n)
                {
                    var buffer = {|#0:new byte[n]|};
                    _ = buffer;
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<UseArrayPoolAnalyzer>
            .Diagnostic(DiagnosticIds.UseArrayPool)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<UseArrayPoolAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task SmallByteArray_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var buffer = new byte[128];
                    _ = buffer;
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseArrayPoolAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task ByteArrayAsField_NoDiagnostic()
    {
        var source = """
            class C
            {
                private byte[] _buffer = new byte[4096];
            }
            """;

        await CSharpAnalyzerVerifier<UseArrayPoolAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }
}
