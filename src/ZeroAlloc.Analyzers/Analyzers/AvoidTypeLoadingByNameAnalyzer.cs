using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Flags resolving types/assemblies by name at runtime (<c>Type.GetType(string)</c>,
/// <c>Assembly.Load</c>, …) which the trimmer/AOT cannot follow statically. Opt-in (disabled
/// by default) because name-based loading is sometimes intentional (plugins). Stands down when
/// the SDK's own AOT analyzer is already enabled.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidTypeLoadingByNameAnalyzer : DiagnosticAnalyzer
{
    private static readonly HashSet<string> AssemblyLoadMethods = new()
    {
        "Load", "LoadFrom", "LoadFile", "LoadWithPartialName",
    };

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidTypeLoadingByName,
        "Avoid resolving types or assemblies by name",
        "'{0}' resolves a type or assembly by name at runtime, which the trimmer and Native AOT cannot follow statically",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: false);

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
            is not IMethodSymbol method || !method.IsStatic)
            return;

        var containingType = method.ContainingType?.ToDisplayString();

        // Static Type.GetType(string ...) — never the instance object.GetType(), which is AOT-safe.
        bool isTypeGetType = method.Name == "GetType" && containingType == "System.Type";
        bool isAssemblyLoad = containingType == "System.Reflection.Assembly"
            && AssemblyLoadMethods.Contains(method.Name);

        if (!isTypeGetType && !isAssemblyLoad)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, invocation.GetLocation(), $"{method.ContainingType!.Name}.{method.Name}"));
    }
}
