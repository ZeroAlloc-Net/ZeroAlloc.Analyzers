using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>Where a ref struct value cannot live: across an await or a yield.</summary>
internal static class AsyncOrIterator
{
    /// <summary>
    /// True when the innermost function around <paramref name="operation"/> is async or an
    /// iterator. Lambdas cannot be iterators; <paramref name="containingSymbol"/> is the analyzed
    /// member when no lambda or local function encloses the operation.
    /// </summary>
    public static bool IsInAsyncOrIterator(IOperation operation, ISymbol? containingSymbol)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            switch (current)
            {
                case IAnonymousFunctionOperation lambda:
                    return lambda.Symbol.IsAsync;
                case ILocalFunctionOperation local:
                    return local.Symbol.IsAsync || ContainsYield(local.Syntax);
            }
        }

        return containingSymbol is IMethodSymbol method
            && (method.IsAsync || method.DeclaringSyntaxReferences.Any(r => ContainsYield(r.GetSyntax())));
    }

    /// <summary>
    /// True when <paramref name="operation"/> awaits or yields, not counting nested lambdas and
    /// local functions, which run on their own frames.
    /// </summary>
    public static bool ContainsAwaitOrYield(IOperation operation)
    {
        if (operation is IAwaitOperation
            or IReturnOperation { Kind: OperationKind.YieldReturn or OperationKind.YieldBreak }
            or IForEachLoopOperation { IsAsynchronous: true }
            or IUsingOperation { IsAsynchronous: true }
            or IUsingDeclarationOperation { IsAsynchronous: true })
        {
            return true;
        }

        foreach (var child in operation.ChildOperations)
        {
            if (child is not (IAnonymousFunctionOperation or ILocalFunctionOperation) && ContainsAwaitOrYield(child))
                return true;
        }

        return false;
    }

    private static bool ContainsYield(SyntaxNode function) =>
        function.DescendantNodes(node => node == function || node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
            .OfType<YieldStatementSyntax>().Any();
}
