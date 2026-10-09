using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

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
            || GetArrayType(field.Declaration.Type) is not { } arrayType
            || GetElementBraces(declarator.Initializer?.Value) is not { } braces)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Make it a ReadOnlySpan<T> property",
                ct => ReplaceAsync(context.Document, field, declarator, arrayType, braces, ct),
                equivalenceKey: DiagnosticIds.UseReadOnlySpanForConstantTable),
            context.Diagnostics[0]);
    }

    private static ArrayTypeSyntax? GetArrayType(TypeSyntax type) => type switch
    {
        ArrayTypeSyntax array => array,
        NullableTypeSyntax { ElementType: ArrayTypeSyntax array } => array,
        _ => null,
    };

    // The tokens around the elements, so the text between them keeps its comments and line breaks.
    private static (SyntaxToken Open, SyntaxToken Close)? GetElementBraces(ExpressionSyntax? value) => value switch
    {
        InitializerExpressionSyntax initializer => (initializer.OpenBraceToken, initializer.CloseBraceToken),
        ArrayCreationExpressionSyntax { Initializer: { } initializer } => (initializer.OpenBraceToken, initializer.CloseBraceToken),
        ImplicitArrayCreationExpressionSyntax creation => (creation.Initializer.OpenBraceToken, creation.Initializer.CloseBraceToken),
        CollectionExpressionSyntax collection => (collection.OpenBracketToken, collection.CloseBracketToken),
        _ => null,
    };

    private static async Task<Document> ReplaceAsync(
        Document document,
        FieldDeclarationSyntax field,
        VariableDeclaratorSyntax declarator,
        ArrayTypeSyntax arrayType,
        (SyntaxToken Open, SyntaxToken Close) braces,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var modifiers = string.Join(" ", field.Modifiers
            .Where(m => !m.IsKind(SyntaxKind.ReadOnlyKeyword))
            .Select(m => m.Text));
        var elementType = arrayType.ElementType.ToString();
        var inner = braces.Open.SyntaxTree!.GetText(ct)
            .ToString(TextSpan.FromBounds(braces.Open.Span.End, braces.Close.SpanStart));

        // A single-line table is tidied; a multi-line one keeps its layout and comments as written.
        var singleLine = inner.IndexOf('\n') < 0;
        if (singleLine)
            inner = inner.Trim();

        // Collection expressions need C# 12; before that the array creation still lowers to static data.
        var supportsCollectionExpressions = document.Project.ParseOptions is CSharpParseOptions options
            && options.LanguageVersion.MapSpecifiedToEffectiveVersion() >= LanguageVersion.CSharp12;
        var value = supportsCollectionExpressions
            ? $"[{inner}]"
            : singleLine ? $"new {elementType}[] {{ {inner} }}" : $"new {elementType}[] {{{inner}}}";

        var property = SyntaxFactory.ParseMemberDeclaration(
            $"{modifiers} ReadOnlySpan<{elementType}> {declarator.Identifier.Text} => {value};")!
            .WithTriviaFrom(field);

        return document.WithSyntaxRoot(UsingDirectives.EnsureSystem(root.ReplaceNode(field, property)));
    }
}
