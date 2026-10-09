using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

internal static class UsingDirectives
{
    /// <summary>Returns <paramref name="root"/> with <c>using System;</c> added unless the file or a namespace already has it.</summary>
    public static SyntaxNode EnsureSystem(SyntaxNode root)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        var hasSystemUsing = compilationUnit.Usings.Any(u => u.Name?.ToString() == "System")
            || compilationUnit.Members.OfType<BaseNamespaceDeclarationSyntax>()
                .Any(ns => ns.Usings.Any(u => u.Name?.ToString() == "System"));
        if (hasSystemUsing)
            return root;

        // Detect the document's line ending style from existing trivia to stay platform-neutral
        var eol = root.DescendantTrivia()
            .FirstOrDefault(t => t.IsKind(SyntaxKind.EndOfLineTrivia))
            .ToFullString();
        if (string.IsNullOrEmpty(eol)) eol = "\n";

        var usingDirective = SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("System"))
            .WithTrailingTrivia(SyntaxFactory.EndOfLine(eol), SyntaxFactory.EndOfLine(eol));
        return compilationUnit.AddUsings(usingDirective);
    }
}
