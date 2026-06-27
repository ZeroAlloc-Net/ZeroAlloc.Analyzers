using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Flags use of <c>System.Reflection.Emit</c> (DynamicMethod, AssemblyBuilder, ILGenerator,
/// …), which generates IL at runtime — unsupported under Native AOT. Stands down when the
/// SDK's own AOT analyzer is already enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidReflectionEmitAnalyzer : DiagnosticAnalyzer
{
    private const string EmitNamespace = "System.Reflection.Emit";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidReflectionEmit,
        "Avoid runtime IL generation",
        "'{0}' from System.Reflection.Emit generates IL at runtime, which Native AOT does not support",
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

            compilationContext.RegisterSyntaxNodeAction(AnalyzeObjectCreation, SyntaxKind.ObjectCreationExpression);
            compilationContext.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
        });
    }

    private static void AnalyzeObjectCreation(SyntaxNodeAnalysisContext context)
    {
        var creation = (ObjectCreationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(creation, context.CancellationToken).Symbol
            is not IMethodSymbol constructor)
            return;

        var type = constructor.ContainingType;
        if (!IsReflectionEmit(type))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, creation.GetLocation(), type.Name));
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken).Symbol
            is not IMethodSymbol method)
            return;

        // Static factory entry points (e.g. AssemblyBuilder.DefineDynamicAssembly). Instance
        // calls on a builder are downstream of a flagged creation, so they are not re-reported.
        if (!method.IsStatic || !IsReflectionEmit(method.ContainingType))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), method.ContainingType.Name));
    }

    private static bool IsReflectionEmit(INamedTypeSymbol type)
        => type.ContainingNamespace?.ToDisplayString() == EmitNamespace;
}
