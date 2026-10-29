using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidStringConcatInLoopAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidStringConcatInLoop,
        "Avoid string concatenation in loops",
        "String concatenation in a loop causes repeated allocations — use StringBuilder",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeAssignment,
            SyntaxKind.AddAssignmentExpression);
    }

    private static void AnalyzeAssignment(SyntaxNodeAnalysisContext context)
    {
        var assignment = (AssignmentExpressionSyntax)context.Node;

        // Check if left side is string type
        var typeInfo = context.SemanticModel.GetTypeInfo(assignment.Left);
        if (typeInfo.Type?.SpecialType != SpecialType.System_String)
            return;

        // Check if inside a loop
        if (!LoopHelper.IsInsideLoop(assignment))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, assignment.GetLocation()));
    }

}
