using Microsoft.CodeAnalysis.CSharp;
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
    [InlineData("_ = {|#0:config.Get<Settings>(o => o.BindNonPublicProperties = true)|};", "ConfigurationBinder.Get")]
    [InlineData("{|#0:services.Configure<Settings>(config)|};", "OptionsConfigurationServiceCollectionExtensions.Configure")]
    [InlineData("{|#0:services.Configure<Settings>(\"name\", config)|};", "OptionsConfigurationServiceCollectionExtensions.Configure")]
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

    [Theory]
    [InlineData("System.Type t = typeof(Settings); _ = config.Get(t);")]
    [InlineData("System.Type t = typeof(Settings); _ = config.GetValue(t, \"Key\");")]
    [InlineData("object o = settings; config.Bind(o);")]
    [InlineData("config.Bind((object)settings);")]
    public async Task TypeKnownOnlyAtRunTime_NoDiagnostic(string statement)
    {
        // The generator cannot intercept these, so the advice would not work.
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Binding(statement), "net8.0", WithConfiguration);
    }

    [Fact]
    public async Task BinderOlderThan8_NoDiagnostic()
    {
        var older = ReferenceAssemblies.Net.Net80.AddPackages([
            new PackageIdentity("Microsoft.Extensions.Configuration.Binder", "7.0.4"),
            new PackageIdentity("Microsoft.Extensions.Options.ConfigurationExtensions", "7.0.0")]);
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Binding("config.Bind(settings);"), "net8.0", older);
    }

    [Theory]
    [InlineData(LanguageVersion.CSharp11)]
    [InlineData(LanguageVersion.CSharp10)]
    public async Task BelowCSharp12_NoDiagnostic(LanguageVersion languageVersion)
    {
        // The generator relies on interceptors and emits error SYSLIB1102 below C# 12.
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Binding("config.Bind(settings);"), "net8.0", WithConfiguration, languageVersion);
    }

    [Fact]
    public async Task CSharp12_Reports()
    {
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Binding("{|#0:config.Bind(settings)|};"), "net8.0", WithConfiguration, LanguageVersion.CSharp12,
            Expected("ConfigurationBinder.Bind"));
    }

    private static string Nested(string accessibility, string statement) => $$"""
        using System.Collections.Generic;
        using Microsoft.Extensions.Configuration;
        using Microsoft.Extensions.DependencyInjection;

        class C
        {
            {{accessibility}} class Hidden
            {
                public string Name { get; set; } = "";
            }

            void M<T>(IConfiguration config, IServiceCollection services, Hidden hidden)
                where T : class
            {
                {{statement}}
            }
        }
        """;

    [Theory]
    [InlineData("_ = config.Get<T>();")]
    [InlineData("_ = config.Get<List<T>>();")]
    [InlineData("_ = config.Get<Dictionary<string, List<T>>>();")]
    [InlineData("_ = config.Get<T[]>();")]
    [InlineData("_ = config.Get(typeof(List<T>));")]
    [InlineData("_ = config.GetValue<T>(\"Key\");")]
    [InlineData("services.Configure<T>(config);")]
    [InlineData("services.AddOptions<T>().Bind(config);")]
    [InlineData("services.AddOptions<T>().BindConfiguration(\"Section\");")]
    public async Task OpenTypeParameter_NoDiagnostic(string statement)
    {
        // The generator reports warning SYSLIB1104 for a type it cannot see at compile time.
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Nested("public", statement), "net8.0", WithConfiguration);
    }

    [Theory]
    [InlineData("private", "_ = config.Get<Hidden>();")]
    [InlineData("private", "_ = config.Get<List<Hidden>>();")]
    [InlineData("private", "_ = config.Get(typeof(Hidden));")]
    [InlineData("private", "config.Bind(hidden);")]
    [InlineData("private", "services.Configure<Hidden>(config);")]
    [InlineData("protected", "_ = config.Get<Hidden>();")]
    [InlineData("private protected", "_ = config.Get<Hidden>();")]
    public async Task InaccessibleType_NoDiagnostic(string accessibility, string statement)
    {
        // Generated code lives outside the type, so it cannot name a private or protected type.
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Nested(accessibility, statement), "net8.0", WithConfiguration);
    }

    [Theory]
    [InlineData("public")]
    [InlineData("internal")]
    [InlineData("protected internal")]
    public async Task AccessibleNestedType_Reports(string accessibility)
    {
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            Nested(accessibility, "_ = {|#0:config.Get<Hidden>()|};"), "net8.0", WithConfiguration,
            Expected("ConfigurationBinder.Get"));
    }

    [Fact]
    public async Task WithoutBinderPackages_NoDiagnostic()
    {
        // Only the framework reference: none of the binding APIs exist, so the rule registers nothing.
        const string source = """
            class C
            {
                void M(object config) => System.Console.WriteLine(config);
            }
            """;
        await CSharpAnalyzerVerifier<UseConfigurationBindingGeneratorAnalyzer>.VerifyAnalyzerAsync(
            source, "net8.0", ReferenceAssemblies.Net.Net80);
    }
}
