using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidClosureInLoopsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidClosureInLoops,
        "Avoid closure allocations in loops",
        "Lambda captures variable '{0}' inside a loop — each iteration allocates a closure object",
        DiagnosticCategories.Boxing,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.SimpleLambdaExpression);
        context.RegisterSyntaxNodeAction(AnalyzeLambda, SyntaxKind.ParenthesizedLambdaExpression);
    }

    private static void AnalyzeLambda(SyntaxNodeAnalysisContext context)
    {
        var lambda = (LambdaExpressionSyntax)context.Node;

        if (!LoopHelper.IsInsideLoop(lambda))
            return;

        // Get the data flow analysis for the lambda body
        var dataFlow = context.SemanticModel.AnalyzeDataFlow(lambda.Body);
        if (dataFlow == null || !dataFlow.Succeeded)
            return;

        // Captured variables = variables read inside the lambda that are declared outside it
        var captured = dataFlow.CapturedInside;
        if (captured.IsEmpty)
            return;

        // Find the first captured variable that's declared outside the lambda
        // (i.e., a true closure capture, not a parameter of the lambda itself)
        var lambdaParams = GetLambdaParameterSymbols(context.SemanticModel, lambda);

        foreach (var symbol in captured)
        {
            if (lambdaParams.Contains(symbol))
                continue;

            // Only report for local variables and parameters from enclosing scope
            if (symbol.Kind != SymbolKind.Local && symbol.Kind != SymbolKind.Parameter)
                continue;

            context.ReportDiagnostic(
                Diagnostic.Create(Rule, lambda.ArrowToken.GetLocation(), symbol.Name));
            return; // Report once per lambda, not per captured variable
        }
    }

    private static ImmutableHashSet<ISymbol> GetLambdaParameterSymbols(
        SemanticModel model,
        LambdaExpressionSyntax lambda)
    {
        var builder = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);

        switch (lambda)
        {
            case SimpleLambdaExpressionSyntax simple:
            {
                var symbol = model.GetDeclaredSymbol(simple.Parameter);
                if (symbol != null)
                    builder.Add(symbol);
                break;
            }
            case ParenthesizedLambdaExpressionSyntax parens:
            {
                foreach (var param in parens.ParameterList.Parameters)
                {
                    var symbol = model.GetDeclaredSymbol(param);
                    if (symbol != null)
                        builder.Add(symbol);
                }
                break;
            }
        }

        return builder.ToImmutable();
    }

}
