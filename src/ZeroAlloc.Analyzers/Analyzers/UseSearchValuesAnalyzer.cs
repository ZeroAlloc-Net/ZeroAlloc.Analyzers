using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseSearchValuesAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseSearchValues,
        "Use SearchValues or FrozenSet for repeated lookups",
        "Repeated string/char comparisons can be replaced with SearchValues<char> or FrozenSet<string>",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    private const int MinChainedComparisons = 4;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (!TfmHelper.TryGetTfm(compilationContext.Options, out var tfm) || !TfmHelper.IsNet8OrLater(tfm))
                return;

            compilationContext.RegisterSyntaxNodeAction(AnalyzeBinaryExpression, SyntaxKind.LogicalOrExpression);
        });
    }

    private static void AnalyzeBinaryExpression(SyntaxNodeAnalysisContext context)
    {
        var binaryExpr = (BinaryExpressionSyntax)context.Node;

        // Only analyze the top-level || chain (skip if parent is also ||)
        if (binaryExpr.Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.LogicalOrExpression })
            return;

        // Count chained == comparisons against the same variable
        var comparisons = CollectEqualityComparisons(binaryExpr);
        if (comparisons.Count < MinChainedComparisons)
            return;

        // Verify all comparisons are against the same variable and string literals
        var firstLeft = comparisons[0].left;
        var allSameVariable = true;
        var allStringLiterals = true;

        foreach (var (left, right) in comparisons)
        {
            if (left != firstLeft)
                allSameVariable = false;

            var typeInfo = context.SemanticModel.GetTypeInfo(right);
            if (typeInfo.Type?.SpecialType != SpecialType.System_String)
                allStringLiterals = false;
        }

        if (allSameVariable && allStringLiterals)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, binaryExpr.GetLocation()));
        }
    }

    private static List<(string left, ExpressionSyntax right)> CollectEqualityComparisons(BinaryExpressionSyntax expr)
    {
        var result = new List<(string, ExpressionSyntax)>();
        CollectComparisonsRecursive(expr, result);
        return result;
    }

    private static void CollectComparisonsRecursive(ExpressionSyntax expr, List<(string, ExpressionSyntax)> result)
    {
        if (expr is BinaryExpressionSyntax binary)
        {
            if (binary.IsKind(SyntaxKind.LogicalOrExpression))
            {
                CollectComparisonsRecursive(binary.Left, result);
                CollectComparisonsRecursive(binary.Right, result);
            }
            else if (binary.IsKind(SyntaxKind.EqualsExpression))
            {
                result.Add((binary.Left.ToString(), binary.Right));
            }
        }
    }
}
