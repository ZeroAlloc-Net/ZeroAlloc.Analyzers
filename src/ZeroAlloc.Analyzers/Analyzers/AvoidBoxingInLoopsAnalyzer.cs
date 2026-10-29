using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidBoxingInLoopsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidBoxingInLoops,
        "Avoid boxing value types in loops",
        "Value type '{0}' is boxed to '{1}' inside a loop — consider using a generic overload or caching",
        DiagnosticCategories.Boxing,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    private static readonly SymbolDisplayFormat ShortFormat = SymbolDisplayFormat.MinimallyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (!LoopHelper.IsInsideLoop(invocation))
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        var arguments = invocation.ArgumentList.Arguments;
        var parameters = method.Parameters;

        for (int i = 0; i < arguments.Count && i < parameters.Length; i++)
        {
            var param = parameters[i];

            // Handle params: once we hit the params parameter, all remaining args map to it
            var effectiveParam = param;
            if (i >= parameters.Length - 1 && parameters[parameters.Length - 1].IsParams)
            {
                effectiveParam = parameters[parameters.Length - 1];
                // For params object[], the element type is object
                if (effectiveParam.Type is IArrayTypeSymbol arrayType
                    && arrayType.ElementType.SpecialType == SpecialType.System_Object)
                {
                    CheckArgForBoxing(context, arguments[i].Expression, arrayType.ElementType);
                    continue;
                }
            }

            // Check if parameter type is object or interface
            if (IsBoxingTarget(effectiveParam.Type))
            {
                CheckArgForBoxing(context, arguments[i].Expression, effectiveParam.Type);
            }
        }
    }

    private static void CheckArgForBoxing(
        SyntaxNodeAnalysisContext context,
        ExpressionSyntax argExpression,
        ITypeSymbol paramType)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(argExpression, context.CancellationToken);
        if (typeInfo.Type != null && typeInfo.Type.IsValueType
            && !typeInfo.Type.IsReferenceType)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, argExpression.GetLocation(),
                    typeInfo.Type.ToDisplayString(ShortFormat), paramType.ToDisplayString(ShortFormat)));
        }
    }

    private static bool IsBoxingTarget(ITypeSymbol type)
    {
        if (type.SpecialType == SpecialType.System_Object)
            return true;

        if (type.TypeKind == TypeKind.Interface)
            return true;

        return false;
    }

}
