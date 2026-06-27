using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1703_AvoidDynamicGenericConstructionTests
{
    [Fact]
    public async Task MakeGenericType_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var t = {|#0:typeof(List<>).MakeGenericType(typeof(int))|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidDynamicGenericConstructionAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidDynamicGenericConstruction)
            .WithLocation(0)
            .WithArguments("MakeGenericType");

        await CSharpAnalyzerVerifier<AvoidDynamicGenericConstructionAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task MakeGenericMethod_Reports()
    {
        var source = """
            using System.Reflection;

            class C
            {
                void G<T>() { }

                void M()
                {
                    MethodInfo mi = typeof(C).GetMethod("G");
                    var g = {|#0:mi.MakeGenericMethod(typeof(int))|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidDynamicGenericConstructionAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidDynamicGenericConstruction)
            .WithLocation(0)
            .WithArguments("MakeGenericMethod");

        await CSharpAnalyzerVerifier<AvoidDynamicGenericConstructionAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task NonGenericReflection_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var name = typeof(int).Name;
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidDynamicGenericConstructionAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StandsDownWhenAotAnalyzerEnabled_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M()
                {
                    var t = typeof(List<>).MakeGenericType(typeof(int));
                }
            }
            """;

        var test = new CSharpAnalyzerTest<AvoidDynamicGenericConstructionAnalyzer, DefaultVerifier>
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
