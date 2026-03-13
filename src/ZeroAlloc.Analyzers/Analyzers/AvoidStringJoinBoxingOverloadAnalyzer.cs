using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidStringJoinBoxingOverloadAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidStringJoinBoxingOverload,
        "Avoid string.Join resolving to params object[] overload",
        "string.Join resolves to the 'params object[]' overload — each element is boxed; use .Select(x => x.ToString()) or cast to IEnumerable<string>",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "Join")
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        // Must be string.Join
        if (method.ContainingType?.SpecialType != SpecialType.System_String)
            return;

        // string.Join needs at least a separator + values argument
        if (invocation.ArgumentList.Arguments.Count < 2)
            return;

        // Get the type of the values argument (second argument)
        var valuesArg = invocation.ArgumentList.Arguments[1];
        var valuesType = context.SemanticModel.GetTypeInfo(valuesArg.Expression, context.CancellationToken).Type;
        if (valuesType is null)
            return;

        // If the values argument is an array, check if element type is non-string
        if (valuesType is IArrayTypeSymbol arrayType)
        {
            if (arrayType.ElementType.SpecialType != SpecialType.System_String)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(Rule, memberAccess.Name.GetLocation()));
            }
            return;
        }

        // If the values argument implements IEnumerable<T>, check if T is non-string
        var enumerableElementType = GetEnumerableElementType(valuesType, context.SemanticModel.Compilation);
        if (enumerableElementType is not null && enumerableElementType.SpecialType != SpecialType.System_String)
        {
            context.ReportDiagnostic(
                Diagnostic.Create(Rule, memberAccess.Name.GetLocation()));
        }
    }

    private static ITypeSymbol? GetEnumerableElementType(ITypeSymbol type, Compilation compilation)
    {
        // Check if type itself is IEnumerable<T>
        if (type is INamedTypeSymbol namedType)
        {
            var elementType = FindEnumerableElementType(namedType, compilation);
            if (elementType is not null)
                return elementType;
        }

        // Check interfaces
        foreach (var iface in type.AllInterfaces)
        {
            var elementType = FindEnumerableElementType(iface, compilation);
            if (elementType is not null)
                return elementType;
        }

        return null;
    }

    private static ITypeSymbol? FindEnumerableElementType(INamedTypeSymbol type, Compilation compilation)
    {
        var genericEnumerable = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
        if (genericEnumerable is null)
            return null;

        if (type.IsGenericType &&
            SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, genericEnumerable) &&
            type.TypeArguments.Length == 1)
        {
            return type.TypeArguments[0];
        }

        return null;
    }
}
