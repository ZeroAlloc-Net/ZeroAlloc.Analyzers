using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CacheEnumGetNameAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.CacheEnumGetName,
        "Cache Enum.GetName/GetValues result outside loops",
        "'{0}' allocates on each call — cache the result outside the loop",
        DiagnosticCategories.Enums,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Must be inside a loop
        if (!LoopHelper.IsInsideLoop(invocation))
            return;

        // Get the method symbol
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var methodName = methodSymbol.Name;
        if (methodName != "GetName" && methodName != "GetValues")
            return;

        var containingType = methodSymbol.ContainingType;
        if (containingType == null || containingType.Name != "Enum")
            return;

        if (!IsSystemNamespace(containingType.ContainingNamespace))
            return;

        var displayName = "Enum." + methodName;
        var diagnostic = Diagnostic.Create(Rule, invocation.GetLocation(), displayName);
        context.ReportDiagnostic(diagnostic);
    }

    private static bool IsSystemNamespace(INamespaceSymbol ns)
    {
        return ns != null
               && ns.Name == "System"
               && ns.ContainingNamespace != null
               && ns.ContainingNamespace.IsGlobalNamespace;
    }

}
