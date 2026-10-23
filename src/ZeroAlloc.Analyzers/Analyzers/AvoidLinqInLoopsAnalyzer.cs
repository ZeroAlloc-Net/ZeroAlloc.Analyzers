using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidLinqInLoopsAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidLinqInLoops,
        "Avoid LINQ methods in loops",
        "LINQ method '{0}' inside a loop allocates on every iteration — consider caching or using a loop-based approach",
        DiagnosticCategories.Linq,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ImmutableHashSet<string> LinqMethodNames = ImmutableHashSet.Create(
        "Where",
        "Select",
        "SelectMany",
        "ToList",
        "ToArray",
        "ToDictionary",
        "ToHashSet",
        "OrderBy",
        "OrderByDescending",
        "ThenBy",
        "GroupBy",
        "First",
        "FirstOrDefault",
        "Last",
        "LastOrDefault",
        "Single",
        "SingleOrDefault",
        "Any",
        "All",
        "Count",
        "Sum",
        "Min",
        "Max",
        "Average",
        "Distinct",
        "Union",
        "Intersect",
        "Except",
        "Concat",
        "Skip",
        "Take",
        "Reverse",
        "Zip");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Must be a member access (e.g., list.Where(...))
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        var methodName = memberAccess.Name.Identifier.Text;

        // Quick syntactic check before expensive semantic analysis
        if (!LinqMethodNames.Contains(methodName))
            return;

        // Must be inside a loop
        if (!IsInsideLoop(invocation))
            return;

        // Verify the method is from System.Linq.Enumerable or System.Linq.Queryable
        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
            return;

        var containingType = methodSymbol.ContainingType;
        if (containingType is null)
            return;

        var containingNamespace = containingType.ContainingNamespace;
        if (containingNamespace is null)
            return;

        if (!IsSystemLinqNamespace(containingNamespace))
            return;

        if (containingType.Name is not ("Enumerable" or "Queryable"))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, memberAccess.Name.GetLocation(), methodName));
    }

    private static bool IsSystemLinqNamespace(INamespaceSymbol ns)
    {
        return ns.Name == "Linq"
               && ns.ContainingNamespace?.Name == "System"
               && ns.ContainingNamespace.ContainingNamespace?.IsGlobalNamespace == true;
    }

    private static bool IsInsideLoop(SyntaxNode node)
    {
        var child = node;
        var current = node.Parent;
        while (current != null)
        {
            if (IsRepeatedPartOfLoop(current, child))
                return true;

            // Stop at method/lambda boundaries
            if (current is MethodDeclarationSyntax
                or LocalFunctionStatementSyntax
                or LambdaExpressionSyntax)
            {
                return false;
            }

            child = current;
            current = current.Parent;
        }

        return false;
    }

    // True when 'child' is a part of 'loop' that is evaluated on every iteration.
    // A foreach collection expression and a for statement's declaration and initializers
    // run once, so they do not count; an enclosing loop further up still can.
    private static bool IsRepeatedPartOfLoop(SyntaxNode loop, SyntaxNode child)
    {
        switch (loop)
        {
            case CommonForEachStatementSyntax forEach:
                return child != forEach.Expression;
            case ForStatementSyntax forStatement:
                return child != forStatement.Declaration
                       && !(child is ExpressionSyntax initializer
                            && forStatement.Initializers.Contains(initializer));
            case WhileStatementSyntax:
            case DoStatementSyntax:
                return true;
            default:
                return false;
        }
    }
}
