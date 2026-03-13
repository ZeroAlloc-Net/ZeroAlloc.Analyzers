using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidMultipleEnumerationAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidMultipleEnumeration,
        "Avoid multiple enumeration of IEnumerable",
        "'{0}' is an IEnumerable<T> that is foreach'd multiple times — each iteration restarts the sequence; call .ToList() or .ToArray() first to materialize once",
        DiagnosticCategories.Linq,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ImmutableHashSet<string> LazyInterfaces = ImmutableHashSet.Create(
        "IEnumerable`1",
        "IQueryable`1");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeMethodBody, SyntaxKind.MethodDeclaration);
    }

    // Known limitation: if a local is reassigned between two foreach loops (query = GetNewSequence()),
    // ZA0607 will still report. Full data-flow analysis would be required to suppress these cases.
    private static void AnalyzeMethodBody(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body is null && method.ExpressionBody is null)
            return;

        // Collect all foreach statements in this method body, stopping at nested
        // local functions and lambdas so their foreaches are not grouped with ours.
        var foreachStatements = method.DescendantNodes(n =>
                n is not LocalFunctionStatementSyntax and not LambdaExpressionSyntax)
            .OfType<ForEachStatementSyntax>()
            .ToImmutableArray();

        if (foreachStatements.Length < 2)
            return;

        // Group foreach statements by the local symbol they iterate over
        var symbolToForeaches = new Dictionary<ISymbol, List<ForEachStatementSyntax>>(SymbolEqualityComparer.Default);

        foreach (var forEach in foreachStatements)
        {
            // Only care about simple identifier expressions
            if (forEach.Expression is not IdentifierNameSyntax identifier)
                continue;

            var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;

            // Only local variables (not parameters, fields)
            if (symbol is not ILocalSymbol localSymbol)
                continue;

            // Only flag lazy interface types
            if (!IsLazyInterfaceType(localSymbol.Type))
                continue;

            if (!symbolToForeaches.TryGetValue(symbol, out var list))
            {
                list = new List<ForEachStatementSyntax>();
                symbolToForeaches[symbol] = list;
            }
            list.Add(forEach);
        }

        // Report on the 2nd+ foreach for each multiply-iterated local
        foreach (var kvp in symbolToForeaches)
        {
            var symbol = kvp.Key;
            var foreaches = kvp.Value;
            if (foreaches.Count < 2)
                continue;

            for (int i = 1; i < foreaches.Count; i++)
            {
                var expr = (IdentifierNameSyntax)foreaches[i].Expression;
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, expr.GetLocation(), symbol.Name));
            }
        }
    }

    private static bool IsLazyInterfaceType(ITypeSymbol type)
        => type.TypeKind == TypeKind.Interface
           && type is INamedTypeSymbol { IsGenericType: true } named
           && LazyInterfaces.Contains(named.MetadataName);
}
