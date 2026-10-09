using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class PreferParamsSpanCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.PreferParamsSpan];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];

        // Changing a signature other assemblies call is a binary breaking change.
        if (diagnostic.Properties.TryGetValue(PreferParamsSpanAnalyzer.ExposedProperty, out var exposed) && exposed == "true")
            return;

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root?.FindNode(diagnostic.Location.SourceSpan).FirstAncestorOrSelf<ParameterSyntax>() is not
            { Type: ArrayTypeSyntax arrayType } parameter)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Declare as params ReadOnlySpan<T>",
                ct => ReplaceAsync(context.Document, parameter, arrayType, ct),
                equivalenceKey: DiagnosticIds.PreferParamsSpan),
            diagnostic);
    }

    private static async Task<Document> ReplaceAsync(Document document, ParameterSyntax parameter, ArrayTypeSyntax arrayType, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var spanType = SyntaxFactory.ParseTypeName($"ReadOnlySpan<{arrayType.ElementType}>").WithTriviaFrom(arrayType);
        var newRoot = root.ReplaceNode(parameter, parameter.WithType(spanType));
        return document.WithSyntaxRoot(UsingDirectives.EnsureSystem(newRoot));
    }
}
