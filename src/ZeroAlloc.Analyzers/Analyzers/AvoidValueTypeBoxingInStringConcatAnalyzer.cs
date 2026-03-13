using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidValueTypeBoxingInStringConcatAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidValueTypeBoxingInStringConcat,
        "Avoid value type boxing in string concatenation",
        "Value type '{0}' is boxed in string concatenation — use string interpolation ($\"...\") or .ToString() to avoid the heap allocation",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBinaryExpression, SyntaxKind.AddExpression);
    }

    private static void AnalyzeBinaryExpression(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;

        var leftType = context.SemanticModel.GetTypeInfo(binary.Left, context.CancellationToken).Type;
        var rightType = context.SemanticModel.GetTypeInfo(binary.Right, context.CancellationToken).Type;

        if (leftType is null || rightType is null)
            return;

        // Verify the operator resolves to string concatenation
        if (context.SemanticModel.GetSymbolInfo(binary, context.CancellationToken).Symbol
                is not IMethodSymbol { ContainingType.SpecialType: SpecialType.System_String })
            return;

        bool leftIsString = leftType.SpecialType == SpecialType.System_String;
        bool rightIsString = rightType.SpecialType == SpecialType.System_String;

        // One side must be string, the other a value type (not string)
        if (leftIsString && rightType.IsValueType && rightType.SpecialType != SpecialType.System_String)
        {
            var typeName = rightType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            context.ReportDiagnostic(Diagnostic.Create(Rule, binary.OperatorToken.GetLocation(), typeName));
        }
        else if (rightIsString && leftType.IsValueType && leftType.SpecialType != SpecialType.System_String)
        {
            var typeName = leftType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            context.ReportDiagnostic(Diagnostic.Create(Rule, binary.OperatorToken.GetLocation(), typeName));
        }
    }
}
