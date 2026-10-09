using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class UseReadOnlySpanForConstantTableCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.UseReadOnlySpanForConstantTable];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        var declarator = root?.FindNode(context.Diagnostics[0].Location.SourceSpan)
            .FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        if (declarator?.Parent?.Parent is not FieldDeclarationSyntax field
            || field.Declaration.Type is not ArrayTypeSyntax arrayType
            || GetElements(declarator.Initializer?.Value) is not { } elements)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Make it a ReadOnlySpan<T> property",
                ct => ReplaceAsync(context.Document, field, declarator, arrayType, elements, ct),
                equivalenceKey: DiagnosticIds.UseReadOnlySpanForConstantTable),
            context.Diagnostics[0]);
    }

    private static IEnumerable<ExpressionSyntax>? GetElements(ExpressionSyntax? value) => value switch
    {
        InitializerExpressionSyntax initializer => initializer.Expressions,
        ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
        ImplicitArrayCreationExpressionSyntax creation => creation.Initializer.Expressions,
        CollectionExpressionSyntax collection when collection.Elements.All(e => e is ExpressionElementSyntax) =>
            collection.Elements.Cast<ExpressionElementSyntax>().Select(e => e.Expression),
        _ => null,
    };

    private static async Task<Document> ReplaceAsync(
        Document document,
        FieldDeclarationSyntax field,
        VariableDeclaratorSyntax declarator,
        ArrayTypeSyntax arrayType,
        IEnumerable<ExpressionSyntax> elements,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var modifiers = string.Join(" ", field.Modifiers
            .Where(m => !m.IsKind(SyntaxKind.ReadOnlyKeyword))
            .Select(m => m.Text));
        var values = string.Join(", ", elements.Select(e => e.ToString()));
        var property = SyntaxFactory.ParseMemberDeclaration(
            $"{modifiers} ReadOnlySpan<{arrayType.ElementType}> {declarator.Identifier.Text} => [{values}];")!
            .WithTriviaFrom(field);

        return document.WithSyntaxRoot(UsingDirectives.EnsureSystem(root.ReplaceNode(field, property)));
    }
}
