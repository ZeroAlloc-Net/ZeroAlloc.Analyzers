using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1707_UseConfigurationBindingGeneratorTests
{
    private static readonly ReferenceAssemblies WithConfiguration = ReferenceAssemblies.Net.Net80.AddPackages([
        new PackageIdentity("Microsoft.Extensions.Configuration.Binder", "8.0.0"),
        new PackageIdentity("Microsoft.Extensions.Options.ConfigurationExtensions", "8.0.0")]);

    private static string Binding(string statement) => $$"""
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;

        class Settings
        {
            public string Name { get; set; } = "";
        }

        class C
        {
            void M(IConfiguration config, IServiceCollection services, Settings settings)
            {
                {{statement}}
            }
        }
        """;

    private static DiagnosticResult Expected(string api) =>
        CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>
            .Diagnostic(DiagnosticIds.UseConfigurationBindingGenerator)
            .WithLocation(0)
            .WithArguments(api);

    [Theory]
    [InlineData("{|#0:config.Bind(settings)|};", "ConfigurationBinder.Bind")]
    [InlineData("_ = {|#0:config.Get<Settings>()|};", "ConfigurationBinder.Get")]
    [InlineData("_ = {|#0:config.Get(typeof(Settings))|};", "ConfigurationBinder.Get")]
    [InlineData("_ = {|#0:config.GetValue<int>(\"Port\")|};", "ConfigurationBinder.GetValue")]
    [InlineData("{|#0:services.Configure<Settings>(config)|};", "OptionsConfigurationServiceCollectionExtensions.Configure")]
    [InlineData("{|#0:services.AddOptions<Settings>().Bind(config)|};", "OptionsBuilderConfigurationExtensions.Bind")]
    [InlineData("{|#0:services.AddOptions<Settings>().BindConfiguration(\"Settings\")|};", "OptionsBuilderConfigurationExtensions.BindConfiguration")]
    public async Task ReflectionBinding_Reports(string statement, string api)
    {
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>
            .VerifyAnalyzerAsync(Binding(statement), "net8.0", WithConfiguration, Expected(api));
    }

    [Theory]
    [InlineData("EnableConfigurationBindingGenerator")]
    [InlineData("PublishAot")]
    [InlineData("IsAotCompatible")]
    public async Task GeneratorOnOrSdkAnalyzerOn_NoDiagnostic(string property)
    {
        // PublishAot turns the generator on by itself, and IsAotCompatible turns on the SDK's IL2026.
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Binding("config.Bind(settings);"),
            WithConfiguration,
            new Dictionary<string, string> { ["TargetFramework"] = "net8.0", [property] = "true" },
            []);
    }

    [Fact]
    public async Task ConfigureWithDelegate_NoDiagnostic()
    {
        // Configure(Action<T>) lives in OptionsServiceCollectionExtensions and binds nothing.
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Binding("services.Configure<Settings>(s => s.Name = \"x\");"), "net8.0", WithConfiguration);
    }
}
