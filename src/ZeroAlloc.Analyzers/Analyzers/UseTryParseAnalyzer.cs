using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseTryParseAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseTryParse,
        "Use TryParse instead of catching the exception from Parse",
        "Use '{0}.TryParse' instead of catching the exception from '{0}.Parse'",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // Exceptions Parse throws for bad input, and the base types that also catch them.
    private static readonly string[] ParseExceptionNames =
    [
        "System.Exception",
        "System.SystemException",
        "System.FormatException",
        "System.OverflowException",
        "System.ArgumentException",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
            foreach (var name in ParseExceptionNames)
            {
                if (start.Compilation.GetTypeByMetadataName(name) is { } type)
                    builder.Add(type);
            }

            var parseExceptions = builder.ToImmutable();
            start.RegisterOperationAction(context => AnalyzeInvocation(context, parseExceptions), OperationKind.Invocation);
        });
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, ImmutableArray<INamedTypeSymbol> parseExceptions)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var parse = invocation.TargetMethod;
        if (parse.Name != "Parse" || !parse.IsStatic || parse.ReturnsVoid || !HasMatchingTryParse(parse))
            return;

        if (!IsSwallowedByCatch(invocation, parseExceptions))
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            invocation.Syntax.GetLocation(),
            parse.ContainingType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    // TryParse must take Parse's parameters followed by 'out' of Parse's return type.
    private static bool HasMatchingTryParse(IMethodSymbol parse)
    {
        foreach (var candidate in parse.ContainingType.GetMembers("TryParse").OfType<IMethodSymbol>())
        {
            if (!candidate.IsStatic || candidate.Arity != parse.Arity || candidate.Parameters.Length != parse.Parameters.Length + 1)
                continue;

            var tryParse = candidate.Arity > 0 ? candidate.Construct(parse.TypeArguments.ToArray()) : candidate;
            var result = tryParse.Parameters[tryParse.Parameters.Length - 1];
            if (result.RefKind != RefKind.Out || !SymbolEqualityComparer.Default.Equals(result.Type, parse.ReturnType))
                continue;

            var parametersMatch = true;
            for (var i = 0; i < parse.Parameters.Length; i++)
            {
                if (!SymbolEqualityComparer.Default.Equals(tryParse.Parameters[i].Type, parse.Parameters[i].Type))
                {
                    parametersMatch = false;
                    break;
                }
            }

            if (parametersMatch)
                return true;
        }

        return false;
    }

    // True when the nearest catch that handles a parse failure swallows it instead of rethrowing.
    private static bool IsSwallowedByCatch(IOperation invocation, ImmutableArray<INamedTypeSymbol> parseExceptions)
    {
        for (var current = invocation; current.Parent is { } parent; current = parent)
        {
            if (parent is IAnonymousFunctionOperation or ILocalFunctionOperation)
                return false;

            if (parent is not ITryOperation tryOperation || tryOperation.Body != current)
                continue;

            foreach (var catchClause in tryOperation.Catches)
            {
                if (catchClause.Filter is not null || !Catches(catchClause.ExceptionType, parseExceptions))
                    continue;

                return !catchClause.Handler.Descendants().OfType<IThrowOperation>().Any();
            }
        }

        return false;
    }

    private static bool Catches(ITypeSymbol? exceptionType, ImmutableArray<INamedTypeSymbol> parseExceptions) =>
        exceptionType is null
        || exceptionType.SpecialType == SpecialType.System_Object
        || (exceptionType is INamedTypeSymbol named && parseExceptions.Contains(named, SymbolEqualityComparer.Default));
}
