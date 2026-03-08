using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0002_UseFrozenSetTests
{
    [Fact]
    public async Task ReadonlyHashSetField_NeverMutated_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                private readonly HashSet<string> {|#0:_tags|} = new() { "a", "b", "c" };

                bool Has(string tag) => _tags.Contains(tag);
            }
            """;

        var expected = CSharpAnalyzerVerifier<UseFrozenSetAnalyzer>
            .Diagnostic(DiagnosticIds.UseFrozenSet)
            .WithLocation(0)
            .WithArguments("_tags");

        await CSharpAnalyzerVerifier<UseFrozenSetAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task ReadonlyHashSetField_OnNet6_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                private readonly HashSet<string> _tags = new() { "a" };

                bool Has(string tag) => _tags.Contains(tag);
            }
            """;

        await CSharpAnalyzerVerifier<UseFrozenSetAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net6.0");
    }

    [Fact]
    public async Task MutableHashSet_WithAddCalls_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                private readonly HashSet<string> _tags = new();

                void AddTag(string tag) => _tags.Add(tag);
            }
            """;

        await CSharpAnalyzerVerifier<UseFrozenSetAnalyzer>
            .VerifyNoDiagnosticAsync(source);
    }
}
