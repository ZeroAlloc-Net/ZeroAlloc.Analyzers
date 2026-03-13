using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidSpanInAsyncMethodAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidSpanInAsyncMethod,
        "Avoid Span<T> in async methods",
        "'{0}' is a Span<T> in an async method — Span<T> cannot safely cross await boundaries; use Memory<T> or ReadOnlyMemory<T> instead",
        DiagnosticCategories.Async,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;

        // Must be async
        if (!method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            return;

        // Must have at least one await expression in the body
        var hasAwait = method.DescendantNodes()
            .Any(n => n.IsKind(SyntaxKind.AwaitExpression));

        if (!hasAwait)
            return;

        // Check parameters
        foreach (var parameter in method.ParameterList.Parameters)
        {
            if (parameter.Type is null) continue;

            var paramType = context.SemanticModel.GetTypeInfo(parameter.Type, context.CancellationToken).Type;
            if (IsSpanType(paramType))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, parameter.Type.GetLocation(), parameter.Identifier.Text));
            }
        }

        // Check local variable declarations in method body (not in nested local functions/lambdas)
        if (method.Body is null) return;

        foreach (var localDecl in method.Body.DescendantNodes(n =>
            n is not LocalFunctionStatementSyntax and not LambdaExpressionSyntax)
            .OfType<LocalDeclarationStatementSyntax>())
        {
            var typeSyntax = localDecl.Declaration.Type;
            var declaredType = context.SemanticModel.GetTypeInfo(typeSyntax, context.CancellationToken).Type;

            if (!IsSpanType(declaredType)) continue;

            foreach (var variable in localDecl.Declaration.Variables)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, typeSyntax.GetLocation(), variable.Identifier.Text));
            }
        }
    }

    private static bool IsSpanType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol { IsGenericType: true } named)
            return false;

        var name = named.OriginalDefinition.ToDisplayString();
        return name is "System.Span<T>" or "System.ReadOnlySpan<T>";
    }
}
