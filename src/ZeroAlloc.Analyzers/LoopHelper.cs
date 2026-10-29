using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers;

/// <summary>Decides whether a syntax node executes once per loop iteration.</summary>
internal static class LoopHelper
{
    /// <summary>
    /// True when <paramref name="node"/> runs on every iteration of an enclosing loop. A loop
    /// body, a <c>for</c> condition or incrementor and a <c>while</c> or <c>do</c> condition
    /// count. A <c>foreach</c> collection expression and a <c>for</c> declaration or initializer
    /// run once, so they do not; an outer loop further up still can. The walk stops at a method,
    /// local function or lambda boundary.
    /// </summary>
    public static bool IsInsideLoop(SyntaxNode node)
    {
        var child = node;
        var current = node.Parent;
        while (current != null)
        {
            if (IsRepeatedPartOfLoop(current, child))
                return true;

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
