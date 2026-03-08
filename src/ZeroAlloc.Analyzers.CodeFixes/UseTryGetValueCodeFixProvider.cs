using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class UseTryGetValueCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.UseTryGetValue];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root == null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        if (node is not InvocationExpressionSyntax invocation)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use TryGetValue",
                ct => ConvertToTryGetValueAsync(context.Document, invocation, ct),
                equivalenceKey: DiagnosticIds.UseTryGetValue),
            diagnostic);
    }

    private static async Task<Document> ConvertToTryGetValueAsync(
        Document document,
        InvocationExpressionSyntax containsKeyInvocation,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root == null) return document;

        var memberAccess = (MemberAccessExpressionSyntax)containsKeyInvocation.Expression;
        var dictExpr = memberAccess.Expression;
        var keyArg = containsKeyInvocation.ArgumentList.Arguments[0];

        // Build: dict.TryGetValue(key, out var value)
        var tryGetValueExpr = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                dictExpr,
                SyntaxFactory.IdentifierName("TryGetValue")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList(new[]
                {
                    keyArg,
                    SyntaxFactory.Argument(
                        null,
                        SyntaxFactory.Token(SyntaxKind.OutKeyword),
                        SyntaxFactory.DeclarationExpression(
                            SyntaxFactory.IdentifierName("var"),
                            SyntaxFactory.SingleVariableDesignation(
                                SyntaxFactory.Identifier("value"))))
                })));

        // Replace the ContainsKey call with TryGetValue
        var newRoot = root.ReplaceNode(containsKeyInvocation, tryGetValueExpr);

        // Replace dict["key"] with value in the if body
        var ifStatement = containsKeyInvocation.FirstAncestorOrSelf<IfStatementSyntax>();
        if (ifStatement != null)
        {
            // Re-find the if statement in the new tree
            var updatedIf = newRoot.FindNode(ifStatement.Span) as IfStatementSyntax;
            if (updatedIf?.Statement != null)
            {
                var rewriter = new IndexerToValueRewriter(dictExpr.ToString(), keyArg.ToString());
                var newBody = (StatementSyntax)rewriter.Visit(updatedIf.Statement);
                newRoot = newRoot.ReplaceNode(updatedIf.Statement, newBody);
            }
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private sealed class IndexerToValueRewriter(string dictText, string keyText) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            if (node.Expression.ToString() == dictText
                && node.ArgumentList.Arguments.Count == 1
                && node.ArgumentList.Arguments[0].ToString() == keyText)
            {
                return SyntaxFactory.IdentifierName("value")
                    .WithTriviaFrom(node);
            }

            return base.VisitElementAccessExpression(node);
        }
    }
}
