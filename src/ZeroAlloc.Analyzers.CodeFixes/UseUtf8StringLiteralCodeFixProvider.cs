using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class UseUtf8StringLiteralCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.UseUtf8StringLiteral];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        // The invocation's span equals its enclosing Argument's span, so the innermost node is needed.
        if (root?.FindNode(context.Diagnostics[0].Location.SourceSpan, getInnermostNodeForTie: true) is not InvocationExpressionSyntax invocation)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use a UTF-8 string literal",
                ct => ReplaceAsync(context.Document, invocation, ct),
                equivalenceKey: DiagnosticIds.UseUtf8StringLiteral),
            context.Diagnostics[0]);
    }

    private static async Task<Document> ReplaceAsync(Document document, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        var model = await document.GetSemanticModelAsync(ct);
        var root = await document.GetSyntaxRootAsync(ct);
        if (model is null || root is null)
            return document;

        var argument = invocation.ArgumentList.Arguments[0].Expression;
        if (model.GetConstantValue(argument, ct) is not { HasValue: true, Value: string text })
            return document;

        var literal = SyntaxFactory.ParseExpression(SymbolDisplay.FormatLiteral(text, quote: true) + "u8")
            .WithTriviaFrom(invocation);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, literal));
    }
}
