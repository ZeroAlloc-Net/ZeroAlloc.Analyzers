using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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
            || !CanDeconstruct(model, loop, access)
            || MutatesDictionary(model, loop, access, context.CancellationToken))
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

    private static readonly ImmutableHashSet<string> MutatingMethods =
        ["TryAdd", "GetOrAdd", "AddOrUpdate", "TryUpdate", "TryRemove", "Clear"];

    // Keys and Values are a point-in-time snapshot, so a loop that writes to the dictionary ends.
    // Enumerating the dictionary is live and may see the loop's own additions, maybe forever.
    private static bool MutatesDictionary(
        SemanticModel model,
        ForEachStatementSyntax loop,
        MemberAccessExpressionSyntax access,
        CancellationToken ct)
    {
        if (model.GetOperation(loop.Statement, ct) is not { } body)
            return false;

        var dictionary = ReferencedSymbol(model.GetOperation(access.Expression, ct));
        var dictionaryType = model.GetTypeInfo(access.Expression, ct).Type;

        foreach (var operation in body.DescendantsAndSelf())
        {
            var instance = operation switch
            {
                IPropertyReferenceOperation { Property.IsIndexer: true } indexer when IsWritten(indexer) => indexer.Instance,
                IInvocationOperation invocation when MutatingMethods.Contains(invocation.TargetMethod.Name) => invocation.Instance,
                _ => null,
            };

            if (instance is null
                || !SymbolEqualityComparer.Default.Equals(instance.Type, dictionaryType))
            {
                continue;
            }

            // When the dictionary is not a simple variable, any write to one of its type may be to it.
            var target = ReferencedSymbol(instance);
            if (dictionary is null || target is null || SymbolEqualityComparer.Default.Equals(dictionary, target))
                return true;
        }

        return false;
    }

    private static bool IsWritten(IOperation operation) => operation.Parent switch
    {
        ISimpleAssignmentOperation assignment => assignment.Target == operation,
        ICompoundAssignmentOperation compound => compound.Target == operation,
        ICoalesceAssignmentOperation coalesce => coalesce.Target == operation,
        IIncrementOrDecrementOperation => true,
        _ => false,
    };

    private static ISymbol? ReferencedSymbol(IOperation? operation) => operation switch
    {
        ILocalReferenceOperation local => local.Local,
        IParameterReferenceOperation parameter => parameter.Parameter,
        IFieldReferenceOperation field => field.Field,
        IPropertyReferenceOperation { Property.IsIndexer: false } property => property.Property,
        _ => null,
    };

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
