using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidEnumToStringAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidEnumToString,
        "Avoid Enum.ToString() — allocates a string",
        "Enum.ToString() allocates on every call — consider using a cached lookup or nameof()",
        DiagnosticCategories.Enums,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation,
            SyntaxKind.InvocationExpression);
        context.RegisterSyntaxNodeAction(AnalyzeStringConcat,
            SyntaxKind.AddExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
            return;

        if (memberAccess.Name.Identifier.Text != "ToString")
            return;

        if (invocation.ArgumentList.Arguments.Count != 0)
            return;

        var type = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;

        if (!IsEnumLike(type))
            return;

        var diagnostic = Diagnostic.Create(Rule, memberAccess.Name.GetLocation());
        context.ReportDiagnostic(diagnostic);
    }

    /// <summary>
    /// Concatenating a string with an enum emits an implicit <c>ToString()</c> call — the exact
    /// allocation this rule exists to flag — but there is no invocation node in the source for
    /// <see cref="AnalyzeInvocation"/> to match, so the cost was invisible. See #51.
    /// </summary>
    private static void AnalyzeStringConcat(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;

        // Only string concatenation lowers to ToString; a user-defined + does not.
        if (context.SemanticModel.GetSymbolInfo(binary, context.CancellationToken).Symbol
                is not IMethodSymbol { ContainingType.SpecialType: SpecialType.System_String })
            return;

        var leftType = context.SemanticModel.GetTypeInfo(binary.Left, context.CancellationToken).Type;
        var rightType = context.SemanticModel.GetTypeInfo(binary.Right, context.CancellationToken).Type;

        if (leftType is null || rightType is null)
            return;

        bool leftIsString = leftType.SpecialType == SpecialType.System_String;
        bool rightIsString = rightType.SpecialType == SpecialType.System_String;

        // An explicit .ToString() makes the operand a string, so AnalyzeInvocation has already
        // reported it and this path correctly finds nothing to add.
        if ((leftIsString && IsEnumLike(rightType)) || (rightIsString && IsEnumLike(leftType)))
            context.ReportDiagnostic(Diagnostic.Create(Rule, binary.OperatorToken.GetLocation()));
    }

    /// <summary>
    /// True for an enum, and for a type parameter constrained to one. Every <c>T</c> satisfying
    /// <c>where T : struct, Enum</c> is an enum at runtime, so <c>T.ToString()</c> carries the
    /// same allocation — but its <see cref="ITypeSymbol.TypeKind"/> is
    /// <see cref="TypeKind.TypeParameter"/>, never <see cref="TypeKind.Enum"/>, which is why the
    /// constrained form was missed entirely. See #52.
    /// </summary>
    private static bool IsEnumLike(ITypeSymbol? type)
    {
        if (type is null)
            return false;

        if (type.TypeKind == TypeKind.Enum)
            return true;

        if (type is ITypeParameterSymbol typeParameter)
        {
            foreach (var constraint in typeParameter.ConstraintTypes)
            {
                if (constraint.SpecialType == SpecialType.System_Enum
                    || constraint.TypeKind == TypeKind.Enum)
                    return true;
            }
        }

        return false;
    }
}
