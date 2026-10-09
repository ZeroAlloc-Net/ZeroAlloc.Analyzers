using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AvoidConcurrentDictionarySnapshotCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.AvoidConcurrentDictionarySnapshot];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken);
        if (root?.FindNode(context.Diagnostics[0].Location.SourceSpan, getInnermostNodeForTie: true) is not MemberAccessExpressionSyntax access
            || access.Parent is not ForEachStatementSyntax loop
            || loop.Expression != access
            || model is null
            || !CanDeconstruct(model, loop, access))
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Enumerate the dictionary",
                ct => ReplaceAsync(context.Document, loop, access, ct),
                equivalenceKey: DiagnosticIds.AvoidConcurrentDictionarySnapshot),
            context.Diagnostics[0]);
    }

    // The fix deconstructs KeyValuePair, so the loop variable must keep its type and Deconstruct must exist.
    private static bool CanDeconstruct(SemanticModel model, ForEachStatementSyntax loop, MemberAccessExpressionSyntax access)
    {
        if (loop.Identifier.Text == "_")
            return false;

        var keyValuePair = model.Compilation.GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2");
        if (keyValuePair is null || keyValuePair.GetMembers("Deconstruct").IsEmpty)
            return false;

        if (loop.Type.IsVar)
            return true;

        var elementType = (model.GetTypeInfo(access).Type as INamedTypeSymbol)?.AllInterfaces
            .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
            ?.TypeArguments[0];
        return SymbolEqualityComparer.Default.Equals(model.GetTypeInfo(loop.Type).Type, elementType);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        ForEachStatementSyntax loop,
        MemberAccessExpressionSyntax access,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var name = SyntaxFactory.SingleVariableDesignation(SyntaxFactory.Identifier(loop.Identifier.Text));
        var discard = SyntaxFactory.DiscardDesignation();
        var isKeys = access.Name.Identifier.Text == "Keys";
        var designation = SyntaxFactory.ParenthesizedVariableDesignation(
            SyntaxFactory.SeparatedList<VariableDesignationSyntax>(
                [isKeys ? name : discard, isKeys ? discard : name]));
        var variable = SyntaxFactory.DeclarationExpression(SyntaxFactory.IdentifierName("var"), designation)
            .WithLeadingTrivia(loop.Type.GetLeadingTrivia())
            .WithTrailingTrivia(loop.Identifier.TrailingTrivia);

        var replacement = SyntaxFactory.ForEachVariableStatement(
            loop.AttributeLists,
            loop.AwaitKeyword,
            loop.ForEachKeyword,
            loop.OpenParenToken,
            variable,
            loop.InKeyword,
            access.Expression.WithTriviaFrom(access),
            loop.CloseParenToken,
            loop.Statement);

        return document.WithSyntaxRoot(root.ReplaceNode(loop, replacement));
    }
}
