using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1701_AvoidExpressionCompileTests
{
    [Fact]
    public async Task ExpressionCompile_Reports()
    {
        var source = """
            using System;
            using System.Linq.Expressions;

            class C
            {
                void M()
                {
                    Expression<Func<int>> e = () => 42;
                    var f = {|#0:e.Compile()|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidExpressionCompileAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidExpressionCompile)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<AvoidExpressionCompileAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task UserDefinedCompileMethod_NoDiagnostic()
    {
        var source = """
            class Shader
            {
                public void Compile() { }
            }

            class C
            {
                void M()
                {
                    new Shader().Compile();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidExpressionCompileAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StandsDownWhenAotAnalyzerEnabled_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Linq.Expressions;

            class C
            {
                void M()
                {
                    Expression<Func<int>> e = () => 42;
                    var f = e.Compile();
                }
            }
            """;

        var test = new CSharpAnalyzerTest<AvoidExpressionCompileAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", """
                is_global = true
                build_property.TargetFramework = net8.0
                build_property.PublishAot = true
                """));

        await test.RunAsync();
    }
}
