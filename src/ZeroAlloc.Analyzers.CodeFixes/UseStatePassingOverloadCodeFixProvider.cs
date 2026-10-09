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

        var body = lambda.Body.ToFullString().Trim();
        string newLambda;
        if (fix == UseStatePassingOverloadAnalyzer.GetOrAddFix)
        {
            // k => ...  becomes  static (k, captured) => ...
            newLambda = $"static ({FirstParameterName(lambda)}, {captured}) => {body}";
        }
        else
        {
            // _ => ...  becomes  static captured => ...
            newLambda = $"static {captured} => {body}";
        }

        var arguments = new List<string>();
        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            arguments.Add(argument == lambdaArgument
                ? (argument.NameColon is { } name ? $"{name.ToString().Trim()} " : string.Empty) + newLambda
                : argument.ToString());
        }

        arguments.Add(captured);
        if (fix == UseStatePassingOverloadAnalyzer.QueueUserWorkItemFix)
            arguments.Add("preferLocal: false");

        var newArgumentList = SyntaxFactory.ParseArgumentList($"({string.Join(", ", arguments)})")
            .WithTriviaFrom(invocation.ArgumentList);

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, invocation.WithArgumentList(newArgumentList)));
    }

    private static string FirstParameterName(LambdaExpressionSyntax lambda) => lambda switch
    {
        SimpleLambdaExpressionSyntax simple => simple.Parameter.Identifier.Text,
        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters[0].Identifier.Text,
        _ => "_",
    };
}
