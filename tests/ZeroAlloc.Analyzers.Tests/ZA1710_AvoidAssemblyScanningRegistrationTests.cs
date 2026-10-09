using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1710_AvoidAssemblyScanningRegistrationTests
{
    private static readonly ReferenceAssemblies WithLibraries = ReferenceAssemblies.Net.Net80.AddPackages([
        new PackageIdentity("Scrutor", "4.2.2"),
        new PackageIdentity("MediatR", "12.4.1"),
        new PackageIdentity("FluentValidation.DependencyInjectionExtensions", "11.11.0"),
        new PackageIdentity("AutoMapper", "13.0.1")]);

    private static string Registration(string statement, string extra = "") => $$"""
        using System;
        using System.Reflection;
        using Microsoft.Extensions.DependencyInjection;
        using Scrutor;
        using MediatR;
        using FluentValidation;
        using AutoMapper;

        class C
        {
            void M(IServiceCollection services)
            {
                {{statement}}
            }
        }
        {{extra}}
        """;

    private static DiagnosticResult Expected(string api, string package) =>
        CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidAssemblyScanningRegistration)
            .WithLocation(0)
            .WithArguments(api, package);

    [Theory]
    [InlineData("{|#0:services.Scan(s => s.FromAssemblyOf<C>().AddClasses())|};", "ServiceCollectionExtensions.Scan", "Inject")]
    [InlineData("services.AddMediatR(cfg => {|#0:cfg.RegisterServicesFromAssembly(typeof(C).Assembly)|});", "MediatRServiceConfiguration.RegisterServicesFromAssembly", "Mediator")]
    [InlineData("services.AddMediatR(cfg => {|#0:cfg.RegisterServicesFromAssemblies(typeof(C).Assembly)|});", "MediatRServiceConfiguration.RegisterServicesFromAssemblies", "Mediator")]
    [InlineData("services.AddMediatR(cfg => {|#0:cfg.RegisterServicesFromAssemblyContaining<C>()|});", "MediatRServiceConfiguration.RegisterServicesFromAssemblyContaining", "Mediator")]
    [InlineData("services.AddMediatR(cfg => {|#0:cfg.RegisterServicesFromAssemblyContaining(typeof(C))|});", "MediatRServiceConfiguration.RegisterServicesFromAssemblyContaining", "Mediator")]
    [InlineData("{|#0:services.AddValidatorsFromAssembly(typeof(C).Assembly)|};", "ServiceCollectionExtensions.AddValidatorsFromAssembly", "Validation")]
    [InlineData("{|#0:services.AddValidatorsFromAssemblies(new[] { typeof(C).Assembly })|};", "ServiceCollectionExtensions.AddValidatorsFromAssemblies", "Validation")]
    [InlineData("{|#0:services.AddValidatorsFromAssemblyContaining<C>()|};", "ServiceCollectionExtensions.AddValidatorsFromAssemblyContaining", "Validation")]
    [InlineData("{|#0:services.AddValidatorsFromAssemblyContaining(typeof(C))|};", "ServiceCollectionExtensions.AddValidatorsFromAssemblyContaining", "Validation")]
    [InlineData("{|#0:services.AddAutoMapper(typeof(C).Assembly)|};", "ServiceCollectionExtensions.AddAutoMapper", "Mapping")]
    [InlineData("{|#0:services.AddAutoMapper(typeof(C))|};", "ServiceCollectionExtensions.AddAutoMapper", "Mapping")]
    [InlineData("{|#0:services.AddAutoMapper(cfg => { }, typeof(C).Assembly)|};", "ServiceCollectionExtensions.AddAutoMapper", "Mapping")]
    public async Task ScanningRegistration_Reports(string statement, string api, string package)
    {
        await CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>
            .VerifyAnalyzerAsync(Registration(statement), "net8.0", WithLibraries, Expected(api, package));
    }

    [Fact]
    public async Task AutoMapperConfigOnly_NoDiagnostic()
    {
        await CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>.VerifyAnalyzerAsync(
            Registration("services.AddAutoMapper(cfg => { });"), "net8.0", WithLibraries);
    }

    [Fact]
    public async Task UserDefinedScan_NoDiagnostic()
    {
        // Lives in the test assembly, so the assembly-name check skips it.
        await CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>.VerifyAnalyzerAsync(
            Registration(
                "services.Scan(1);",
                "static class Ext { public static void Scan(this IServiceCollection s, int x) { } }")
                .Replace("using Scrutor;", ""),
            "net8.0", WithLibraries);
    }

    [Fact]
    public async Task UserDefinedAddValidatorsFromAssembly_NoDiagnostic()
    {
        await CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>.VerifyAnalyzerAsync(
            Registration(
                "services.AddValidatorsFromAssembly(typeof(C).Assembly);",
                "static class Ext { public static void AddValidatorsFromAssembly(this IServiceCollection s, Assembly a) { } }")
                .Replace("using FluentValidation;", ""),
            "net8.0", WithLibraries);
    }

    [Fact]
    public async Task PublishAot_UnannotatedScan_StillReports()
    {
        // The libraries are not trim-annotated, so the SDK analyzer says nothing about these calls.
        await CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            Registration("{|#0:services.Scan(s => s.FromAssemblyOf<C>().AddClasses())|};"),
            WithLibraries,
            new Dictionary<string, string> { ["TargetFramework"] = "net8.0", ["PublishAot"] = "true" },
            [Expected("ServiceCollectionExtensions.Scan", "Inject")]);
    }

    // Stands in for a library whose scanning API is trim-annotated: the project is named like
    // the Scrutor assembly, and the method carries RequiresUnreferencedCode.
    private const string AnnotatedScrutor = """
        namespace System.Diagnostics.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.All)]
            public sealed class RequiresUnreferencedCodeAttribute : System.Attribute
            {
                public RequiresUnreferencedCodeAttribute(string message) { }
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            public static class ServiceCollectionExtensions
            {
                [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("x")]
                public static void Scan(this IServiceCollection services, int x) { }
            }
        }
        """;

    [Theory]
    [InlineData("true", false)]
    [InlineData("false", true)]
    public async Task AnnotatedScan_StandsDownOnlyWhenSdkAnalyzerIsOn(string publishAot, bool reports)
    {
        var test = new CSharpAnalyzerTest<AvoidAssemblyScanningRegistrationAnalyzer, DefaultVerifier>
        {
            TestCode = $$"""
                using Microsoft.Extensions.DependencyInjection;

                class C
                {
                    void M(IServiceCollection services)
                    {
                        {{(reports ? "{|#0:services.Scan(1)|};" : "services.Scan(1);")}}
                    }
                }
                """,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80.AddPackages([
                new PackageIdentity("Microsoft.Extensions.DependencyInjection.Abstractions", "8.0.0")]),
        };

        // Only the annotated stand-in is referenced, so Scan(1) binds to it and not to Scrutor.
        test.TestState.AdditionalProjects["Scrutor"].Sources.Add(AnnotatedScrutor);
        test.TestState.AdditionalProjectReferences.Add("Scrutor");
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true\nbuild_property.PublishAot = {publishAot}\n"));
        if (reports)
            test.ExpectedDiagnostics.Add(Expected("ServiceCollectionExtensions.Scan", "Inject"));

        await test.RunAsync();
    }
}
