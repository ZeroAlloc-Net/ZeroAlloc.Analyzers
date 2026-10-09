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
                // The generator ships with Microsoft.Extensions.Configuration.Binder 8.0.
                if (start.Compilation.GetTypeByMetadataName(typeName) is { } type
                    && type.ContainingAssembly.Identity.Version.Major >= 8)
                {
                    builder.Add((type, methods));
                }
            }

            if (builder.Count == 0)
                return;

            var apis = builder.ToImmutable();
            start.RegisterOperationAction(context => AnalyzeInvocation(context, apis), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<(INamedTypeSymbol Type, string[] Methods)> apis)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        foreach (var (type, methods) in apis)
        {
            if (SymbolEqualityComparer.Default.Equals(method.ContainingType, type) && Array.IndexOf(methods, method.Name) >= 0)
            {
                // The generator only replaces calls whose target type is known at compile time.
                if (HasRuntimeOnlyTarget(invocation, method))
                    return;

                context.ReportDiagnostic(Diagnostic.Create(
                    Rule,
                    context.Operation.Syntax.GetLocation(),
                    $"{type.Name}.{method.Name}"));
                return;
            }
        }
    }

    private static bool HasRuntimeOnlyTarget(IInvocationOperation invocation, IMethodSymbol method)
    {
        foreach (var argument in invocation.Arguments)
        {
            var value = Unwrap(argument.Value);
            if (argument.Parameter is not { } parameter)
                continue;

            if (parameter.Type.ToDisplayString() == "System.Type" && value is not ITypeOfOperation)
                return true;

            if (method.Name == "Bind" && parameter.Name == "instance" && value.Type?.SpecialType == SpecialType.System_Object)
                return true;
        }

        return false;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (operation is IConversionOperation { IsImplicit: true } conversion)
            operation = conversion.Operand;
        return operation;
    }
}
