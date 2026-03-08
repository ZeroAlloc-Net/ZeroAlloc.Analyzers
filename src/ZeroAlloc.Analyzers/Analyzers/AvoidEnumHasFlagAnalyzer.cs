using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidEnumHasFlagAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidEnumHasFlag,
        "Avoid Enum.HasFlag — use bitwise check",
        "Enum.HasFlag boxes the argument on older runtimes — use '(value & flag) != 0' instead",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax
            {
                Name.Identifier.Text: "HasFlag"
            })
        {
            return;
        }

        // Verify it's Enum.HasFlag
        var symbol = context.SemanticModel.GetSymbolInfo(invocation).Symbol;
        if (symbol is not IMethodSymbol method
            || method.ContainingType.SpecialType != SpecialType.System_Enum)
        {
            return;
        }

        // On net7.0+, HasFlag is JIT-intrinsic — no boxing, no diagnostic
        if (TfmHelper.TryGetTfm(context.Options, out var tfm) && TfmHelper.IsNetOrLater(tfm, 7))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation()));
    }
}
