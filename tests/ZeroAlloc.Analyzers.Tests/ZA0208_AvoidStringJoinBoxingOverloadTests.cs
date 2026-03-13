using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0208_AvoidStringJoinBoxingOverloadTests
{
    [Fact]
    public async Task StringJoin_ListOfInt_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var numbers = new List<int> { 1, 2, 3 };
                    var result = string.{|#0:Join|}(", ", numbers);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringJoinBoxingOverload)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringJoin_ArrayOfInt_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    int[] numbers = new int[] { 1, 2, 3 };
                    var result = string.{|#0:Join|}(", ", numbers);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringJoinBoxingOverload)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringJoin_CharSeparator_ListOfInt_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var numbers = new List<int> { 1, 2, 3 };
                    var result = string.{|#0:Join|}(',', numbers);
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidStringJoinBoxingOverload)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StringJoin_ListOfString_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var words = new List<string> { "a", "b" };
                    var result = string.Join(", ", words);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StringJoin_ArrayOfString_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    string[] words = new string[] { "a", "b" };
                    var result = string.Join(", ", words);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StringJoin_IEnumerableOfString_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(IEnumerable<string> words)
                {
                    var result = string.Join(", ", words);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidStringJoinBoxingOverloadAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
