using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Simplification;

namespace ZeroAlloc.Analyzers.CodeFixes;

/// <summary>
/// Type names for code fixes that stay resolvable wherever they are inserted. The generator emits
/// the name fully qualified, with the symbol and <see cref="Simplifier.Annotation"/> annotations.
/// <see cref="Simplifier.AddImportsAnnotation"/> then makes code action cleanup import the namespace
/// where no using, global using or enclosing namespace brings it into scope, and simplify the name.
/// </summary>
internal static class ImportedTypeSyntax
{
    /// <summary>The qualified name of <paramref name="type"/>, imported and simplified on cleanup.</summary>
    public static TypeSyntax For(Document document, ITypeSymbol type) =>
        ((TypeSyntax)SyntaxGenerator.GetGenerator(document).TypeExpression(type))
            .WithAdditionalAnnotations(Simplifier.Annotation, Simplifier.AddImportsAnnotation);

    /// <summary>
    /// The qualified name of <paramref name="genericType"/> with <paramref name="typeArgument"/>,
    /// which is kept as written instead of being qualified.
    /// </summary>
    /// <remarks>
    /// The symbol annotation names the unconstructed type. A type argument such as a method's own
    /// type parameter would not resolve after a fix changes that method's signature, and the
    /// import would then be skipped.
    /// </remarks>
    public static TypeSyntax For(Document document, INamedTypeSymbol genericType, TypeSyntax typeArgument)
    {
        var qualified = For(document, genericType.OriginalDefinition);
        var generic = qualified.DescendantNodesAndSelf().OfType<GenericNameSyntax>().First();
        return qualified.ReplaceNode(
            generic.TypeArgumentList,
            SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArgument)));
    }
}
