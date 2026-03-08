using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseTryGetValueAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseTryGetValue,
        "Use TryGetValue instead of ContainsKey + indexer",
        "Use 'TryGetValue' instead of 'ContainsKey' followed by indexer access",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeIfStatement, SyntaxKind.IfStatement);
    }

    private static void AnalyzeIfStatement(SyntaxNodeAnalysisContext context)
    {
        var ifStatement = (IfStatementSyntax)context.Node;

        // Look for: if (dict.ContainsKey(key))
        if (ifStatement.Condition is not InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Name.Identifier.Text: "ContainsKey"
                } memberAccess,
                ArgumentList.Arguments.Count: 1
            } containsKeyInvocation)
        {
            return;
        }

        // Verify it's on IDictionary/Dictionary
        var symbolInfo = context.SemanticModel.GetSymbolInfo(containsKeyInvocation);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        var containingType = method.ContainingType;
        if (containingType == null)
            return;

        // Check if the type implements IDictionary<,> or is Dictionary<,>
        if (!IsDictionaryType(containingType))
            return;

        // Get the key argument
        var keyArg = containsKeyInvocation.ArgumentList.Arguments[0].Expression;

        // Look for dict[key] in the if body
        var dictExpr = memberAccess.Expression;
        if (HasIndexerAccessWithSameKey(ifStatement.Statement, dictExpr, keyArg, context.SemanticModel))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, containsKeyInvocation.GetLocation()));
        }
    }

    private static bool IsDictionaryType(INamedTypeSymbol type)
    {
        if (type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>")
            return true;

        foreach (var iface in type.AllInterfaces)
        {
            if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IDictionary<TKey, TValue>")
                return true;
        }

        return false;
    }

    private static bool HasIndexerAccessWithSameKey(
        StatementSyntax body,
        ExpressionSyntax dictExpr,
        ExpressionSyntax keyExpr,
        SemanticModel model)
    {
        foreach (var node in body.DescendantNodes())
        {
            if (node is ElementAccessExpressionSyntax elementAccess
                && elementAccess.ArgumentList.Arguments.Count == 1)
            {
                var indexerDictExpr = elementAccess.Expression;
                var indexerKeyExpr = elementAccess.ArgumentList.Arguments[0].Expression;

                // Compare dictionary expression and key expression textually
                if (indexerDictExpr.ToString() == dictExpr.ToString()
                    && indexerKeyExpr.ToString() == keyExpr.ToString())
                {
                    return true;
                }
            }
        }

        return false;
    }
}
