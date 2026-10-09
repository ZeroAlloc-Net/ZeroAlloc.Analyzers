using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports reflection-based configuration binding while the configuration-binding source
/// generator is off. Stands down when the SDK's own AOT analyzer is enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseConfigurationBindingGeneratorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseConfigurationBindingGenerator,
        "Use the configuration-binding source generator",
        "'{0}' binds configuration with reflection; set <EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator> to generate the binding code",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // The binding entry points the source generator intercepts.
    private static readonly (string Type, string[] Methods)[] BindingApis =
    [
        ("Microsoft.Extensions.Configuration.ConfigurationBinder", ["Bind", "Get", "GetValue"]),
        ("Microsoft.Extensions.DependencyInjection.OptionsBuilderConfigurationExtensions", ["Bind", "BindConfiguration"]),
        ("Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions", ["Configure"]),
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (AotHelper.IsSdkAotAnalyzerEnabled(start.Options)
                || AotHelper.IsBuildPropertyTrue(start.Options, "EnableConfigurationBindingGenerator"))
            {
                return;
            }

            var builder = ImmutableArray.CreateBuilder<(INamedTypeSymbol Type, string[] Methods)>();
            foreach (var (typeName, methods) in BindingApis)
            {
                if (start.Compilation.GetTypeByMetadataName(typeName) is { } type)
                    builder.Add((type, methods));
            }

            if (builder.Count == 0)
                return;

            var apis = builder.ToImmutable();
            start.RegisterOperationAction(context => AnalyzeInvocation(context, apis), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<(INamedTypeSymbol Type, string[] Methods)> apis)
    {
        var method = ((IInvocationOperation)context.Operation).TargetMethod;
        foreach (var (type, methods) in apis)
        {
            if (SymbolEqualityComparer.Default.Equals(method.ContainingType, type) && Array.IndexOf(methods, method.Name) >= 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    context.Operation.Syntax.GetLocation(),
                    $"{type.Name}.{method.Name}"));
                return;
            }
        }
    }
}
