using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1702_AvoidReflectionEmitTests
{
    [Fact]
    public async Task DynamicMethodCreation_Reports()
    {
        var source = """
            using System;
            using System.Reflection.Emit;

            class C
            {
                void M()
                {
                    var d = {|#0:new DynamicMethod("x", typeof(void), Type.EmptyTypes)|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidReflectionEmitAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidReflectionEmit)
            .WithLocation(0)
            .WithArguments("DynamicMethod");

        await CSharpAnalyzerVerifier<AvoidReflectionEmitAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task AssemblyBuilderStaticFactory_Reports()
    {
        var source = """
            using System.Reflection;
            using System.Reflection.Emit;

            class C
            {
                void M()
                {
                    var ab = {|#0:AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("x"), AssemblyBuilderAccess.Run)|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidReflectionEmitAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidReflectionEmit)
            .WithLocation(0)
            .WithArguments("AssemblyBuilder");

        await CSharpAnalyzerVerifier<AvoidReflectionEmitAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task NonEmitType_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var sb = new System.Text.StringBuilder();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidReflectionEmitAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StandsDownWhenAotAnalyzerEnabled_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Reflection.Emit;

            class C
            {
                void M()
                {
                    var d = new DynamicMethod("x", typeof(void), Type.EmptyTypes);
                }
            }
            """;

        var test = new CSharpAnalyzerTest<AvoidReflectionEmitAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", """
                is_global = true
                build_property.TargetFramework = net8.0
                build_property.IsAotCompatible = true
                """));

        await test.RunAsync();
    }
}
