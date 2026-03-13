using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidZeroLengthArrayAllocationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidZeroLengthArrayAllocation,
        "Avoid zero-length array allocation",
        "Allocation of zero-length '{0}' array — use 'Array.Empty<{0}>()' to return a cached singleton instead",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeArrayCreation, SyntaxKind.ArrayCreationExpression);
    }

    private static void AnalyzeArrayCreation(SyntaxNodeAnalysisContext context)
    {
        var arrayCreation = (ArrayCreationExpressionSyntax)context.Node;

        // Check for new T[0]
        var rankSpecifier = arrayCreation.Type.RankSpecifiers.FirstOrDefault();
        if (rankSpecifier != null && rankSpecifier.Sizes.Count == 1)
        {
            var sizeExpr = rankSpecifier.Sizes[0];
            var constantValue = context.SemanticModel.GetConstantValue(sizeExpr, context.CancellationToken);
            if (constantValue.HasValue && constantValue.Value is int size && size == 0)
            {
                ReportDiagnostic(context, arrayCreation);
                return;
            }
        }

        // Check for new T[] { } — array with empty initializer
        if (arrayCreation.Initializer != null && arrayCreation.Initializer.Expressions.Count == 0)
        {
            // Only flag if no explicit size (that case is handled above)
            if (rankSpecifier == null || rankSpecifier.Sizes.All(s => s is OmittedArraySizeExpressionSyntax))
            {
                ReportDiagnostic(context, arrayCreation);
            }
        }
    }

    private static void ReportDiagnostic(SyntaxNodeAnalysisContext context, ArrayCreationExpressionSyntax arrayCreation)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(arrayCreation, context.CancellationToken);
        if (typeInfo.Type is not IArrayTypeSymbol arrayType)
            return;

        var elementTypeName = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, arrayCreation.GetLocation(), elementTypeName));
    }
}
