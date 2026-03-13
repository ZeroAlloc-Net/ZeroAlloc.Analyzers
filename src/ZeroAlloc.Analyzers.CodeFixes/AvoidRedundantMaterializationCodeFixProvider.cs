using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AvoidRedundantMaterializationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.AvoidRedundantMaterialization];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);
        // The diagnostic is on the method name token; walk up to the invocation
        if (node.Parent?.Parent is not InvocationExpressionSyntax invocation)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Remove redundant materialization call",
                ct => RemoveMaterializationCallAsync(context.Document, invocation, ct),
                equivalenceKey: DiagnosticIds.AvoidRedundantMaterialization),
            diagnostic);
    }

    private static async Task<Document> RemoveMaterializationCallAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

        var memberAccess = (MemberAccessExpressionSyntax)invocation.Expression;
        var receiver = memberAccess.Expression.WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, receiver);
        return document.WithSyntaxRoot(newRoot);
    }
}
