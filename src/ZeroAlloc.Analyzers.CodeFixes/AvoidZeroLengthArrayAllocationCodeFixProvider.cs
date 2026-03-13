using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class AvoidZeroLengthArrayAllocationCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.AvoidZeroLengthArrayAllocation];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var node = root.FindNode(diagnostic.Location.SourceSpan);

        if (node is not ArrayCreationExpressionSyntax arrayCreation) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Replace with Array.Empty<T>()",
                ct => ReplaceWithArrayEmptyAsync(context.Document, arrayCreation, ct),
                equivalenceKey: DiagnosticIds.AvoidZeroLengthArrayAllocation),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithArrayEmptyAsync(
        Document document,
        ArrayCreationExpressionSyntax arrayCreation,
        CancellationToken ct)
    {
        var semanticModel = await document.GetSemanticModelAsync(ct);
        if (semanticModel is null) return document;

        var typeInfo = semanticModel.GetTypeInfo(arrayCreation, ct);
        if (typeInfo.Type is not IArrayTypeSymbol arrayType) return document;

        var elementType = arrayType.ElementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        // Build: Array.Empty<T>()
        var arrayEmptyCall = SyntaxFactory.ParseExpression($"Array.Empty<{elementType}>()")
            .WithTriviaFrom(arrayCreation);

        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null) return document;

        var newRoot = root.ReplaceNode(arrayCreation, arrayEmptyCall);

        // Add `using System;` if not present
        var compilationUnit = (CompilationUnitSyntax)newRoot;
        var hasSystemUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "System")
            || compilationUnit.Members.OfType<BaseNamespaceDeclarationSyntax>()
                .Any(ns => ns.Usings.Any(u => u.Name?.ToString() == "System"));
        if (!hasSystemUsing)
        {
            // Detect the document's line ending style from existing trivia to stay platform-neutral
            var eol = root.DescendantTrivia()
                .FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia))
                .ToFullString();
            if (string.IsNullOrEmpty(eol)) eol = "\n";

            var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("System"))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine(eol), SyntaxFactory.EndOfLine(eol));
            newRoot = compilationUnit.AddUsings(usingDirective);
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
