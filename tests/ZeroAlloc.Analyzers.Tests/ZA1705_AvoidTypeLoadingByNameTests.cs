using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1705_AvoidTypeLoadingByNameTests
{
    /// <summary>Enables ZA1705 (disabled by default) via .globalconfig.</summary>
    private static CSharpAnalyzerTest<AvoidTypeLoadingByNameAnalyzer, DefaultVerifier> CreateTest(
        string source,
        bool publishAot = false)
    {
        var test = new CSharpAnalyzerTest<AvoidTypeLoadingByNameAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        var aot = publishAot ? "\nbuild_property.PublishAot = true" : "";
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", $"""
                is_global = true
                build_property.TargetFramework = net8.0
                dotnet_diagnostic.ZA1705.severity = info{aot}
                """));

        return test;
    }

    [Fact]
    public async Task TypeGetTypeByName_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    var t = {|#0:System.Type.GetType("System.String")|};
                }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            CSharpAnalyzerVerifier<AvoidTypeLoadingByNameAnalyzer>
                .Diagnostic(DiagnosticIds.AvoidTypeLoadingByName)
                .WithLocation(0)
                .WithArguments("Type.GetType"));

        await test.RunAsync();
    }

    [Fact]
    public async Task AssemblyLoadByName_Reports()
    {
        var source = """
            class C
            {
                void M()
                {
                    var a = {|#0:System.Reflection.Assembly.Load("SomeAssembly")|};
                }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(
            CSharpAnalyzerVerifier<AvoidTypeLoadingByNameAnalyzer>
                .Diagnostic(DiagnosticIds.AvoidTypeLoadingByName)
                .WithLocation(0)
                .WithArguments("Assembly.Load"));

        await test.RunAsync();
    }

    [Fact]
    public async Task InstanceGetType_NoDiagnostic()
    {
        // object.GetType() is the safe runtime-type query, never name-based loading.
        var source = """
            class C
            {
                void M()
                {
                    var t = this.GetType();
                }
            }
            """;

        await CreateTest(source).RunAsync();
    }

    [Fact]
    public async Task ExplicitlyDisabled_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var t = System.Type.GetType("System.String");
                }
            }
            """;

        var test = new CSharpAnalyzerTest<AvoidTypeLoadingByNameAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", """
                is_global = true
                build_property.TargetFramework = net8.0
                dotnet_diagnostic.ZA1705.severity = none
                """));

        await test.RunAsync();
    }

    [Fact]
    public async Task StandsDownWhenAotAnalyzerEnabled_NoDiagnostic()
    {
        // Even with the rule explicitly enabled, it stands down when the SDK AOT analyzer is on.
        var source = """
            class C
            {
                void M()
                {
                    var t = System.Type.GetType("System.String");
                }
            }
            """;

        await CreateTest(source, publishAot: true).RunAsync();
    }
}
