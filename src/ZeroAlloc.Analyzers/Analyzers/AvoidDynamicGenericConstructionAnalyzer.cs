using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Flags <c>Type.MakeGenericType</c> and <c>MethodInfo.MakeGenericMethod</c>, which
/// instantiate generics at runtime via reflection — requires dynamic code, unsupported under
/// Native AOT. Stands down when the SDK's own AOT analyzer is already enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidDynamicGenericConstructionAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidDynamicGenericConstruction,
        "Avoid constructing generic types or methods at runtime",
        "'{0}' instantiates a generic via reflection at runtime, which requires dynamic code that Native AOT does not support",
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

        var containingType = method.ContainingType?.ToDisplayString();

        bool isMakeGenericType = method.Name == "MakeGenericType" && containingType == "System.Type";
        bool isMakeGenericMethod = method.Name == "MakeGenericMethod" && containingType == "System.Reflection.MethodInfo";

        if (!isMakeGenericType && !isMakeGenericMethod)
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), method.Name));
    }
}
