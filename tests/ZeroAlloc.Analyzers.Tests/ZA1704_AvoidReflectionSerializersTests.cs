using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1704_AvoidReflectionSerializersTests
{
    [Fact]
    public async Task XmlSerializer_Reports()
    {
        var source = """
            using System.Xml.Serialization;

            class C
            {
                void M()
                {
                    var s = {|#0:new XmlSerializer(typeof(C))|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidReflectionSerializersAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidReflectionSerializers)
            .WithLocation(0)
            .WithArguments("XmlSerializer");

        await CSharpAnalyzerVerifier<AvoidReflectionSerializersAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task DataContractSerializer_Reports()
    {
        var source = """
            using System.Runtime.Serialization;

            class C
            {
                void M()
                {
                    var s = {|#0:new DataContractSerializer(typeof(C))|};
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidReflectionSerializersAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidReflectionSerializers)
            .WithLocation(0)
            .WithArguments("DataContractSerializer");

        await CSharpAnalyzerVerifier<AvoidReflectionSerializersAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task UnrelatedType_NoDiagnostic()
    {
        var source = """
            class C
            {
                void M()
                {
                    var ms = new System.IO.MemoryStream();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidReflectionSerializersAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StandsDownWhenAotAnalyzerEnabled_NoDiagnostic()
    {
        var source = """
            using System.Xml.Serialization;

            class C
            {
                void M()
                {
                    var s = new XmlSerializer(typeof(C));
                }
            }
            """;

        var test = new CSharpAnalyzerTest<AvoidReflectionSerializersAnalyzer, DefaultVerifier>
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
