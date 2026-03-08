using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseCollectionsMarshalAsSpanAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseCollectionsMarshalAsSpan,
        "Use CollectionsMarshal.AsSpan for List<T> iteration",
        "Use 'CollectionsMarshal.AsSpan()' to iterate List<T> without enumerator allocation",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (!TfmHelper.TryGetTfm(compilationContext.Options, out var tfm) || !TfmHelper.IsNet5OrLater(tfm))
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeForEach, SyntaxKind.ForEachStatement);
        });
    }

    private static void AnalyzeForEach(SyntaxNodeAnalysisContext context)
    {
        var forEach = (ForEachStatementSyntax)context.Node;

        // Get the type of the collection being iterated
        var typeInfo = context.SemanticModel.GetTypeInfo(forEach.Expression);
        if (typeInfo.Type is not INamedTypeSymbol namedType)
            return;

        // Must be List<T>
        if (namedType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.List<T>")
            return;

        // Check for await expressions in the loop body — Span can't cross await boundaries
        if (forEach.Statement.DescendantNodes().OfType<AwaitExpressionSyntax>().Any())
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, forEach.ForEachKeyword.GetLocation()));
    }
}
