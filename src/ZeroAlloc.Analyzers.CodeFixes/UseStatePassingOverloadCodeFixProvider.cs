using System.Collections.Immutable;
using System.Composition;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class UseStatePassingOverloadCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds => [DiagnosticIds.UseStatePassingOverload];

    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var diagnostic = context.Diagnostics[0];

        // The analyzer decides whether a rewrite keeps the meaning and says so in the properties.
        if (!diagnostic.Properties.TryGetValue(UseStatePassingOverloadAnalyzer.FixProperty, out var fix) || fix is null
            || !diagnostic.Properties.TryGetValue(UseStatePassingOverloadAnalyzer.CapturedProperty, out var captured) || captured is null)
        {
            return;
        }

        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken);
        if (root?.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true) is not LambdaExpressionSyntax lambda
            || lambda.Parent is not ArgumentSyntax lambdaArgument
            || lambdaArgument.Parent?.Parent is not InvocationExpressionSyntax invocation)
        {
            return;
        }

        context.RegisterCodeFix(
            CodeAction.Create(
                "Use the state-passing overload",
                ct => ReplaceAsync(context.Document, invocation, lambdaArgument, lambda, fix, captured, ct),
                equivalenceKey: DiagnosticIds.UseStatePassingOverload),
            diagnostic);
    }

    private static async Task<Document> ReplaceAsync(
        Document document,
        InvocationExpressionSyntax invocation,
        ArgumentSyntax lambdaArgument,
        LambdaExpressionSyntax lambda,
        string fix,
        string captured,
        CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct);
        if (root is null)
            return document;

        var isGetOrAdd = fix == UseStatePassingOverloadAnalyzer.GetOrAddFix;
        var newLambda = MakeStatic(lambda, captured, isGetOrAdd);

        // Positional arguments stay positional; if the call names any argument, the new ones are named
        // too, because a positional argument may not follow a named one that is out of position.
        var anyNamed = invocation.ArgumentList.Arguments.Any(a => a.NameColon is not null);

        // When the lambda starts its own line, the new arguments start theirs, at the same indentation.
        var lineBreak = lambdaArgument.GetLeadingTrivia()
            .Concat(lambdaArgument.GetFirstToken().GetPreviousToken().TrailingTrivia)
            .Where(t => t.IsKind(SyntaxKind.EndOfLineTrivia))
            .Select(t => (SyntaxTrivia?)t)
            .FirstOrDefault();
        var leading = lineBreak is null
            ? SyntaxFactory.TriviaList(SyntaxFactory.Space)
            : lambdaArgument.GetLeadingTrivia();

        var stateName = isGetOrAdd ? "factoryArgument" : "state";
        var newArguments = new List<ArgumentSyntax>
        {
            Argument(captured, anyNamed ? stateName : null, leading),
        };

        if (!isGetOrAdd)
        {
            newArguments.Add(SyntaxFactory.Argument(SyntaxFactory.LiteralExpression(SyntaxKind.FalseLiteralExpression))
                .WithNameColon(SyntaxFactory.NameColon("preferLocal").WithTrailingTrivia(SyntaxFactory.Space))
                .WithLeadingTrivia(leading));
        }

        var replaced = invocation.ReplaceNode(lambda, newLambda);
        var existing = replaced.ArgumentList.Arguments;
        var separator = SyntaxFactory.Token(SyntaxKind.CommaToken);
        if (lineBreak is { } eol)
            separator = separator.WithTrailingTrivia(eol);

        var nodes = existing.Concat(newArguments);
        var separators = existing.GetSeparators().Concat(newArguments.Select(_ => separator));
        var newInvocation = replaced.WithArgumentList(
            replaced.ArgumentList.WithArguments(SyntaxFactory.SeparatedList(nodes, separators)));

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }

    private static ArgumentSyntax Argument(string expression, string? name, SyntaxTriviaList leading)
    {
        var argument = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(Identifier(expression)));
        if (name is not null)
            argument = argument.WithNameColon(SyntaxFactory.NameColon(name).WithTrailingTrivia(SyntaxFactory.Space));

        return argument.WithLeadingTrivia(leading);
    }

    // Edits the lambda in place: adds 'static' (after any attributes, before 'async') and either extends
    // the parameter list with the captured name or renames the single parameter to it.
    private static LambdaExpressionSyntax MakeStatic(LambdaExpressionSyntax lambda, string captured, bool addParameter)
    {
        var leading = lambda.GetLeadingTrivia();
        var stripped = lambda.WithoutLeadingTrivia();

        LambdaExpressionSyntax edited;
        switch (stripped)
        {
            case SimpleLambdaExpressionSyntax simple when addParameter:
                // k => ...  becomes  (k, captured) => ...
                var parameter = simple.Parameter;
                var list = SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(
                    [
                        parameter.WithoutTrivia(),
                        SyntaxFactory.Parameter(Identifier(captured)).WithLeadingTrivia(SyntaxFactory.Space),
                    ],
                    [SyntaxFactory.Token(SyntaxKind.CommaToken)]))
                    .WithCloseParenToken(SyntaxFactory.Token(SyntaxKind.CloseParenToken).WithTrailingTrivia(parameter.GetTrailingTrivia()));
                edited = SyntaxFactory.ParenthesizedLambdaExpression(
                    simple.AttributeLists,
                    simple.Modifiers,
                    returnType: null,
                    list,
                    simple.ArrowToken,
                    simple.Block,
                    simple.ExpressionBody);
                break;

            case SimpleLambdaExpressionSyntax simple:
                edited = simple.WithParameter(Renamed(simple.Parameter, captured));
                break;

            case ParenthesizedLambdaExpressionSyntax parenthesized when addParameter:
                var parameters = parenthesized.ParameterList.Parameters;
                var added = SyntaxFactory.Parameter(Identifier(captured)).WithLeadingTrivia(SyntaxFactory.Space);
                var open = parenthesized.ParameterList;
                edited = parenthesized.WithParameterList(open.WithParameters(
                    SyntaxFactory.SeparatedList(
                        parameters.Concat([added]),
                        parameters.GetSeparators().Concat([SyntaxFactory.Token(SyntaxKind.CommaToken)]))));
                break;

            case ParenthesizedLambdaExpressionSyntax parenthesized:
                edited = parenthesized.WithParameterList(parenthesized.ParameterList.WithParameters(
                    SyntaxFactory.SingletonSeparatedList(Renamed(parenthesized.ParameterList.Parameters[0], captured))));
                break;

            default:
                return lambda;
        }

        var staticToken = SyntaxFactory.Token(SyntaxKind.StaticKeyword).WithTrailingTrivia(SyntaxFactory.Space);
        edited = edited.WithModifiers(edited.Modifiers.Insert(0, staticToken));
        return edited.WithLeadingTrivia(leading);
    }

    private static ParameterSyntax Renamed(ParameterSyntax parameter, string name) =>
        parameter.WithIdentifier(Identifier(name).WithTriviaFrom(parameter.Identifier));

    // The captured name comes from the symbol, without its '@'; a keyword needs it back to stay a name.
    private static SyntaxToken Identifier(string name) =>
        // Contextual keywords such as 'scoped' read as modifiers in a lambda parameter list, so escape those too.
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None && SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None
            ? SyntaxFactory.Identifier(name)
            : SyntaxFactory.ParseToken("@" + name);
}
