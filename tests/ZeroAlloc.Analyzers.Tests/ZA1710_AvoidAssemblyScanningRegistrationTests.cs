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

    private const string InjectHint = "ZeroAlloc.Inject registers services at compile time with a source generator";
    private const string MediatorHint = "ZeroAlloc.Mediator dispatches without reflection; register its handlers with ZeroAlloc.Inject";
    private const string ValidationHint = "ZeroAlloc.Validation registers validators at compile time with a source generator";
    private const string MappingHint = "ZeroAlloc.Mapping generates mappers at compile time, with no registration needed";

    private static DiagnosticResult Expected(string api, string hint) =>
        CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidAssemblyScanningRegistration)
            .WithLocation(0)
            .WithArguments(api, hint);

    [Theory]
    [InlineData("{|#0:services.Scan(s => s.FromAssemblyOf<C>().AddClasses())|};", "ServiceCollectionExtensions.Scan", InjectHint)]
    [InlineData("services.AddMediatR(cfg => {|#0:cfg.RegisterServicesFromAssembly(typeof(C).Assembly)|});", "MediatRServiceConfiguration.RegisterServicesFromAssembly", MediatorHint)]
    [InlineData("services.AddMediatR(cfg => {|#0:cfg.RegisterServicesFromAssemblies(typeof(C).Assembly)|});", "MediatRServiceConfiguration.RegisterServicesFromAssemblies", MediatorHint)]
    [InlineData("services.AddMediatR(cfg => {|#0:cfg.RegisterServicesFromAssemblyContaining<C>()|});", "MediatRServiceConfiguration.RegisterServicesFromAssemblyContaining", MediatorHint)]
    [InlineData("services.AddMediatR(cfg => {|#0:cfg.RegisterServicesFromAssemblyContaining(typeof(C))|});", "MediatRServiceConfiguration.RegisterServicesFromAssemblyContaining", MediatorHint)]
    [InlineData("{|#0:services.AddValidatorsFromAssembly(typeof(C).Assembly)|};", "ServiceCollectionExtensions.AddValidatorsFromAssembly", ValidationHint)]
    [InlineData("{|#0:services.AddValidatorsFromAssemblies(new[] { typeof(C).Assembly })|};", "ServiceCollectionExtensions.AddValidatorsFromAssemblies", ValidationHint)]
    [InlineData("{|#0:services.AddValidatorsFromAssemblyContaining<C>()|};", "ServiceCollectionExtensions.AddValidatorsFromAssemblyContaining", ValidationHint)]
    [InlineData("{|#0:services.AddValidatorsFromAssemblyContaining(typeof(C))|};", "ServiceCollectionExtensions.AddValidatorsFromAssemblyContaining", ValidationHint)]
    [InlineData("{|#0:services.AddAutoMapper(typeof(C).Assembly)|};", "ServiceCollectionExtensions.AddAutoMapper", MappingHint)]
    [InlineData("{|#0:services.AddAutoMapper(typeof(C))|};", "ServiceCollectionExtensions.AddAutoMapper", MappingHint)]
    [InlineData("{|#0:services.AddAutoMapper(cfg => { }, typeof(C).Assembly)|};", "ServiceCollectionExtensions.AddAutoMapper", MappingHint)]
    public async Task ScanningRegistration_Reports(string statement, string api, string hint)
    {
        await CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>
            .VerifyAnalyzerAsync(Registration(statement), "net8.0", WithLibraries, Expected(api, hint));
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
            [Expected("ServiceCollectionExtensions.Scan", InjectHint)]);
    }

    // Stands in for a library whose scanning API is annotated: the project is named like the
    // Scrutor assembly, and the annotation sits where the test puts it.
    private static string AnnotatedScrutor(string typeAnnotation, string methodAnnotation) => $$"""
        namespace System.Diagnostics.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.All)]
            public sealed class RequiresUnreferencedCodeAttribute : System.Attribute
            {
                public RequiresUnreferencedCodeAttribute(string message) { }
            }

            [System.AttributeUsage(System.AttributeTargets.All)]
            public sealed class RequiresDynamicCodeAttribute : System.Attribute
            {
                public RequiresDynamicCodeAttribute(string message) { }
            }
        }

        namespace Microsoft.Extensions.DependencyInjection
        {
            {{typeAnnotation}}
            public static class ServiceCollectionExtensions
            {
                {{methodAnnotation}}
                public static void Scan(this IServiceCollection services, int x) { }
            }
        }
        """;

    private const string Unreferenced = "[System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(\"x\")]";
    private const string DynamicCode = "[System.Diagnostics.CodeAnalysis.RequiresDynamicCode(\"x\")]";

    [Theory]
    [InlineData("", Unreferenced, "PublishAot", "true", false)]
    [InlineData("", Unreferenced, "PublishAot", "false", true)]
    [InlineData("", Unreferenced, "IsAotCompatible", "true", false)]
    [InlineData("", Unreferenced, "EnableTrimAnalyzer", "true", false)]
    [InlineData("", Unreferenced, "PublishTrimmed", "true", false)]
    [InlineData("", Unreferenced, "IsTrimmable", "true", false)]
    [InlineData("", Unreferenced, "EnableAotAnalyzer", "true", true)]
    [InlineData("", DynamicCode, "EnableAotAnalyzer", "true", false)]
    [InlineData("", DynamicCode, "PublishAot", "true", false)]
    [InlineData("", DynamicCode, "EnableTrimAnalyzer", "true", true)]
    [InlineData(DynamicCode, "", "PublishAot", "true", false)]
    [InlineData(DynamicCode, "", "PublishAot", "false", true)]
    [InlineData(Unreferenced, "", "PublishAot", "true", false)]
    public async Task AnnotatedScan_StandsDownOnlyWhenTheMatchingSdkAnalyzerIsOn(
        string typeAnnotation, string methodAnnotation, string property, string value, bool reports)
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
        test.TestState.AdditionalProjects["Scrutor"].Sources.Add(AnnotatedScrutor(typeAnnotation, methodAnnotation));
        test.TestState.AdditionalProjectReferences.Add("Scrutor");
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true\nbuild_property.{property} = {value}\n"));
        if (reports)
            test.ExpectedDiagnostics.Add(Expected("ServiceCollectionExtensions.Scan", InjectHint));

        await test.RunAsync();
    }

    [Fact]
    public async Task MediatR11_AddMediatRWithType_Reports()
    {
        var mediatR11 = ReferenceAssemblies.Net.Net80.AddPackages([
            new PackageIdentity("MediatR.Extensions.Microsoft.DependencyInjection", "11.1.0")]);
        await CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>.VerifyAnalyzerAsync(
            """
            using MediatR;
            using Microsoft.Extensions.DependencyInjection;

            class C
            {
                void M(IServiceCollection services)
                {
                    {|#0:services.AddMediatR(typeof(C))|};
                }
            }
            """,
            "net8.0", mediatR11, Expected("ServiceCollectionExtensions.AddMediatR", MediatorHint));
    }

    [Fact]
    public async Task AutoMapper12_AddAutoMapperWithType_Reports()
    {
        var autoMapper12 = ReferenceAssemblies.Net.Net80.AddPackages([
            new PackageIdentity("AutoMapper.Extensions.Microsoft.DependencyInjection", "12.0.1")]);
        await CSharpAnalyzerVerifier<AvoidAssemblyScanningRegistrationAnalyzer>.VerifyAnalyzerAsync(
            """
            using Microsoft.Extensions.DependencyInjection;

            class C
            {
                void M(IServiceCollection services)
                {
                    {|#0:services.AddAutoMapper(typeof(C))|};
                }
            }
            """,
            "net8.0", autoMapper12, Expected("ServiceCollectionExtensions.AddAutoMapper", MappingHint));
    }
}
