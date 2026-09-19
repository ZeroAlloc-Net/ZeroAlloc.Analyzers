using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidValueTypeBoxingInStringConcatAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidValueTypeBoxingInStringConcat,
        "Avoid value type boxing in string concatenation",
        "Value type '{0}' is boxed in string concatenation — use string interpolation ($\"...\") or .ToString() to avoid the heap allocation",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeBinaryExpression, SyntaxKind.AddExpression);
    }

    private static void AnalyzeBinaryExpression(SyntaxNodeAnalysisContext context)
    {
        var binary = (BinaryExpressionSyntax)context.Node;

        var leftType = context.SemanticModel.GetTypeInfo(binary.Left, context.CancellationToken).Type;
        var rightType = context.SemanticModel.GetTypeInfo(binary.Right, context.CancellationToken).Type;

        if (leftType is null || rightType is null)
            return;

        // Verify the operator resolves to string concatenation
        if (context.SemanticModel.GetSymbolInfo(binary, context.CancellationToken).Symbol
                is not IMethodSymbol { ContainingType.SpecialType: SpecialType.System_String })
            return;

        bool leftIsString = leftType.SpecialType == SpecialType.System_String;
        bool rightIsString = rightType.SpecialType == SpecialType.System_String;

        // One side must be string, the other a value type (not string)
        if (leftIsString && rightType.IsValueType && rightType.SpecialType != SpecialType.System_String
            && !OverridesToString(rightType))
        {
            var typeName = rightType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            context.ReportDiagnostic(Diagnostic.Create(Rule, binary.OperatorToken.GetLocation(), typeName));
        }
        else if (rightIsString && leftType.IsValueType && leftType.SpecialType != SpecialType.System_String
            && !OverridesToString(leftType))
        {
            var typeName = leftType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            context.ReportDiagnostic(Diagnostic.Create(Rule, binary.OperatorToken.GetLocation(), typeName));
        }
    }

    /// <summary>
    /// Boxing in string concatenation happens exactly when the operand type does not override
    /// <c>ToString()</c>, because the compiler must then box to reach <c>object.ToString()</c>.
    /// When an override exists the compiler calls it directly and concatenates two strings.
    ///
    /// Measured on .NET 10 Release, bytes per operation:
    /// <c>"abc" + 123</c> 40 B, identical to <c>"abc" + 123.ToString()</c>;
    /// <c>"abc" + (object)123</c> 64 B, exactly one box larger.
    /// A struct without an override costs 80 B; the same struct with one costs 32 B. See #50.
    /// </summary>
    private static bool OverridesToString(ITypeSymbol type)
    {
        // Every enum inherits System.Enum's override.
        if (type.TypeKind == TypeKind.Enum)
            return true;

        // A value type cannot inherit from another value type, so only its own members matter.
        // Walking to a base would find System.ValueType's override and wrongly clear every struct.
        foreach (var member in type.GetMembers("ToString"))
        {
            if (member is IMethodSymbol { Parameters.Length: 0, IsOverride: true })
                return true;
        }

        return false;
    }
}
