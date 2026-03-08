using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0010_UseTryGetValueTests
{
    [Fact]
    public async Task ContainsKey_FollowedByIndexer_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var dict = new Dictionary<string, int>();
                    if ({|#0:dict.ContainsKey("key")|})
                    {
                        var value = dict["key"];
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<UseTryGetValueAnalyzer>
            .Diagnostic(DiagnosticIds.UseTryGetValue)
            .WithLocation(0)
            .WithMessage("Use 'TryGetValue' instead of 'ContainsKey' followed by indexer access");

        await CSharpAnalyzerVerifier<UseTryGetValueAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task TryGetValue_AlreadyUsed_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var dict = new Dictionary<string, int>();
                    if (dict.TryGetValue("key", out var value))
                    {
                        _ = value;
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseTryGetValueAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task ContainsKey_WithoutIndexer_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var dict = new Dictionary<string, int>();
                    if (dict.ContainsKey("key"))
                    {
                        System.Console.WriteLine("exists");
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseTryGetValueAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }

    [Fact]
    public async Task ContainsKey_FollowedByIndexer_FixesToTryGetValue()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var dict = new Dictionary<string, int>();
                    if ({|#0:dict.ContainsKey("key")|})
                    {
                        var v = dict["key"];
                    }
                }
            }
            """;

        var fixedSource = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var dict = new Dictionary<string, int>();
                    if (dict.TryGetValue("key", out var value))
                    {
                        var v = value;
                    }
                }
            }
            """;

        await CSharpCodeFixVerifier<UseTryGetValueAnalyzer, CodeFixes.UseTryGetValueCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, DiagnosticIds.UseTryGetValue);
    }
}
