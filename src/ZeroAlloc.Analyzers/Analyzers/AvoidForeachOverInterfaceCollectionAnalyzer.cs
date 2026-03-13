using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidForeachOverInterfaceCollectionAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidForeachOverInterfaceCollection,
        "Avoid foreach over interface-typed local collection",
        "Local variable '{0}' is typed as '{1}' — foreach allocates a heap enumerator; use a concrete type (List<T>, T[]) instead",
        DiagnosticCategories.Linq,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ImmutableHashSet<string> CollectionInterfaces = ImmutableHashSet.Create(
        "IEnumerable`1",
        "ICollection`1",
        "IList`1",
        "IReadOnlyCollection`1",
        "IReadOnlyList`1");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeForeach, SyntaxKind.ForEachStatement);
    }

    private static void AnalyzeForeach(SyntaxNodeAnalysisContext context)
    {
        var forEach = (ForEachStatementSyntax)context.Node;

        // Get the type of the foreach expression
        var exprType = context.SemanticModel.GetTypeInfo(forEach.Expression, context.CancellationToken).Type;
        if (exprType is not INamedTypeSymbol namedType)
            return;

        // Must be one of the target interfaces
        if (!IsCollectionInterface(namedType))
            return;

        // The expression must be a simple identifier (local variable reference)
        if (forEach.Expression is not IdentifierNameSyntax identifier)
            return;

        var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
        if (symbol is not ILocalSymbol localSymbol)
            return;

        // Find the variable declarator to check the initializer
        var declaratorRef = localSymbol.DeclaringSyntaxReferences;
        if (declaratorRef.IsEmpty)
            return;

        var declaratorSyntax = declaratorRef[0].GetSyntax(context.CancellationToken);
        if (declaratorSyntax is not VariableDeclaratorSyntax declarator)
            return;

        // Only flag if initialized with a concrete new expression or array creation
        var initializer = declarator.Initializer?.Value;
        if (initializer is null)
            return;

        if (!IsConcreteNewExpression(initializer))
            return;

        var typeName = namedType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        context.ReportDiagnostic(
            Diagnostic.Create(Rule, identifier.GetLocation(),
                localSymbol.Name, typeName));
    }

    private static bool IsCollectionInterface(INamedTypeSymbol type)
        => type.TypeKind == TypeKind.Interface
           && type.IsGenericType
           && CollectionInterfaces.Contains(type.MetadataName);

    private static bool IsConcreteNewExpression(ExpressionSyntax expr)
        => expr is ObjectCreationExpressionSyntax
            or ImplicitObjectCreationExpressionSyntax
            or ArrayCreationExpressionSyntax
            or ImplicitArrayCreationExpressionSyntax
            or CollectionExpressionSyntax;
}
