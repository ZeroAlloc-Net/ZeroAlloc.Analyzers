using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports ValidateDataAnnotations, which validates options with reflection, and points at the
/// [OptionsValidator] source generator. Stands down when the SDK's own AOT analyzer is enabled,
/// and when the generator is unavailable, that is before Microsoft.Extensions.Options 8.0.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseOptionsValidatorGeneratorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseOptionsValidatorGenerator,
        "Use a source-generated options validator",
        "'ValidateDataAnnotations' validates with reflection; use an [OptionsValidator] source-generated validator instead",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            if (AotHelper.IsSdkAotAnalyzerEnabled(start.Options))
                return;

            // The suggested fix only exists with Microsoft.Extensions.Options 8.0 or later.
            if (start.Compilation.GetTypeByMetadataName("Microsoft.Extensions.Options.OptionsValidatorAttribute") is null)
                return;

            var extensions = start.Compilation.GetTypeByMetadataName(
                "Microsoft.Extensions.DependencyInjection.OptionsBuilderDataAnnotationsExtensions");
            if (extensions is null)
                return;

            start.RegisterOperationAction(context =>
            {
                var method = ((IInvocationOperation)context.Operation).TargetMethod;
                if (method.Name == "ValidateDataAnnotations"
                    && SymbolEqualityComparer.Default.Equals(method.ContainingType, extensions))
                {
                    context.ReportDiagnostic(Diagnostic.Create(Rule, context.Operation.Syntax.GetLocation()));
                }
            }, OperationKind.Invocation);
        });
    }
}
