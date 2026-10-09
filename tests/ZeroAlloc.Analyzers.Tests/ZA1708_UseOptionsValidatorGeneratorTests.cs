using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1708_UseOptionsValidatorGeneratorTests
{
    private static readonly ReferenceAssemblies WithOptions = ReferenceAssemblies.Net.Net80.AddPackages([
        new PackageIdentity("Microsoft.Extensions.Options.DataAnnotations", "8.0.0")]);

    private static readonly ReferenceAssemblies WithOptions7 = ReferenceAssemblies.Net.Net80.AddPackages([
        new PackageIdentity("Microsoft.Extensions.Options.DataAnnotations", "7.0.0")]);

    private const string Source = """
        using Microsoft.Extensions.DependencyInjection;

        class Settings
        {
            public string Name { get; set; } = "";
        }

        class C
        {
            void M(IServiceCollection services)
            {
                {|#0:services.AddOptions<Settings>().ValidateDataAnnotations()|};
            }
        }
        """;

    private static string Unmarked => Source.Replace("{|#0:", string.Empty).Replace("|};", ";");

    [Fact]
    public async Task ValidateDataAnnotations_Reports()
    {
        var expected = CSharpAnalyzerVerifier<UseOptionsValidatorGeneratorAnalyzer>
            .Diagnostic(DiagnosticIds.UseOptionsValidatorGenerator)
            .WithLocation(0);

        await CSharpAnalyzerVerifier<UseOptionsValidatorGeneratorAnalyzer>
            .VerifyAnalyzerAsync(Source, "net8.0", WithOptions, expected);
    }

    [Fact]
    public async Task OptionsValidatorUnavailable_NoDiagnostic()
    {
        // Options 7.x has no [OptionsValidator] generator to suggest.
        await CSharpAnalyzerVerifier<UseOptionsValidatorGeneratorAnalyzer>
            .VerifyAnalyzerAsync(Unmarked, "net8.0", WithOptions7);
    }

    [Theory]
    [InlineData("PublishAot")]
    [InlineData("IsAotCompatible")]
    public async Task SdkAotAnalyzerOn_NoDiagnostic(string property)
    {
        // With the SDK's AOT analyzer on, IL2026 already reports ValidateDataAnnotations.
        await CSharpAnalyzerVerifier<UseOptionsValidatorGeneratorAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Unmarked,
            WithOptions,
            new Dictionary<string, string> { ["TargetFramework"] = "net8.0", [property] = "true" },
            []);
    }
}
