using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidStringReplaceChainAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidStringReplaceChain,
        "Avoid chained string.Replace calls",
        "Chained string.Replace calls allocate intermediate strings — use StringBuilder",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    private const int MinChainLength = 3;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Only analyze outermost Replace call (skip if parent is also a .Replace())
        if (invocation.Parent is MemberAccessExpressionSyntax { Name.Identifier.Text: "Replace", Parent: InvocationExpressionSyntax })
            return;

        // Count the chain length
        var chainLength = CountReplaceChain(invocation, context.SemanticModel);
        if (chainLength >= MinChainLength)
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
        }
    }

    private static int CountReplaceChain(InvocationExpressionSyntax invocation, SemanticModel model)
    {
        var count = 0;
        var current = invocation;

        while (current != null)
        {
            if (current.Expression is MemberAccessExpressionSyntax memberAccess
                && memberAccess.Name.Identifier.Text == "Replace")
            {
                // Verify it's string.Replace
                var symbol = model.GetSymbolInfo(current).Symbol;
                if (symbol is IMethodSymbol method
                    && method.ContainingType.SpecialType == SpecialType.System_String)
                {
                    count++;
                    // Follow the chain: the receiver of this .Replace() might be another .Replace()
                    if (memberAccess.Expression is InvocationExpressionSyntax inner)
                    {
                        current = inner;
                        continue;
                    }
                }
            }

            break;
        }

        return count;
    }
}
