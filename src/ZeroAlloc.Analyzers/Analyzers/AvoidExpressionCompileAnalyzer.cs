using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Flags <c>Expression.Compile()</c>, which turns an expression tree into a delegate via
/// runtime code generation — unsupported under Native AOT. Stands down when the SDK's own
/// AOT analyzer is already enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidExpressionCompileAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidExpressionCompile,
        "Avoid compiling expression trees at runtime",
        "Expression.Compile() turns an expression tree into a delegate via runtime code generation, which Native AOT does not support",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (AotHelper.IsSdkAotAnalyzerEnabled(compilationContext.Options))
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
            return;

        if (method.Name != "Compile" || !IsLambdaExpression(method.ContainingType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }

    private static bool IsLambdaExpression(INamedTypeSymbol? type)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            if (t.Name == "LambdaExpression"
                && t.ContainingNamespace?.ToDisplayString() == "System.Linq.Expressions")
            {
                return true;
            }
        }

        return false;
    }
}
