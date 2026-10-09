using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1706_MarkLibraryAotCompatibleTests
{
    private const string Library = """
        public class Widget
        {
        }
        """;

    private const string App = """
        class Program
        {
            static void Main() { }
        }
        """;

    private static DiagnosticResult Expected() =>
        CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.Diagnostic(DiagnosticIds.MarkLibraryAotCompatible);

    private static Dictionary<string, string> Properties(string targetFramework, params (string Name, string Value)[] extra)
    {
        var properties = new Dictionary<string, string> { ["TargetFramework"] = targetFramework };
        foreach (var (name, value) in extra)
            properties[name] = value;
        return properties;
    }

    [Theory]
    [InlineData("net8.0")]
    [InlineData("net9.0")]
    [InlineData("net10.0")]
    [InlineData("net8.0-windows")]
    public async Task Net8OrLaterLibraryWithoutAotProperty_Reports(string targetFramework)
    {
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Library, ReferenceAssemblies.Net.Net80, Properties(targetFramework), [Expected()]);
    }

    [Theory]
    [InlineData("netstandard2.0")]
    [InlineData("net6.0")]
    [InlineData("net472")]
    public async Task OlderTargetFramework_NoDiagnostic(string targetFramework)
    {
        // IsAotCompatible only means something from net8.0, and setting it earlier produces an SDK warning.
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Library, ReferenceAssemblies.Net.Net80, Properties(targetFramework), []);
    }

    [Theory]
    [InlineData("IsAotCompatible", "true")]
    [InlineData("IsAotCompatible", "false")]
    [InlineData("PublishAot", "true")]
    [InlineData("EnableAotAnalyzer", "true")]
    [InlineData("IsTestProject", "true")]
    [InlineData("IsTestProject", "True")]
    public async Task AlreadyDecidedOrTestProject_NoDiagnostic(string name, string value)
    {
        // An explicit IsAotCompatible=false is a deliberate opt-out, so it also stands the rule down.
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Library, ReferenceAssemblies.Net.Net80, Properties("net8.0", (name, value)), []);
    }

    [Theory]
    [InlineData(OutputKind.ConsoleApplication)]
    [InlineData(OutputKind.WindowsApplication)]
    public async Task Application_NoDiagnostic(OutputKind outputKind)
    {
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            App, ReferenceAssemblies.Net.Net80, Properties("net8.0"), [], outputKind);
    }

    [Fact]
    public async Task TargetFrameworkNotVisible_NoDiagnostic()
    {
        // Without the buildTransitive props the analyzer cannot tell the target framework.
        await CSharpAnalyzerVerifier<MarkLibraryAotCompatibleAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Library, ReferenceAssemblies.Net.Net80, new Dictionary<string, string>(), []);
    }
}
