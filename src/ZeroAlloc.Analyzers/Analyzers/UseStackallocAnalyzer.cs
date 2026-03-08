using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseStackallocAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseStackalloc,
        "Use stackalloc for small fixed-size arrays",
        "Small array allocation (size <= 256) can use 'stackalloc' to avoid heap allocation",
        DiagnosticCategories.Memory,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    private const int MaxStackallocSize = 256;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeArrayCreation, SyntaxKind.ArrayCreationExpression);
    }

    private static void AnalyzeArrayCreation(SyntaxNodeAnalysisContext context)
    {
        var arrayCreation = (ArrayCreationExpressionSyntax)context.Node;

        // Must be byte[] or char[]
        var typeInfo = context.SemanticModel.GetTypeInfo(arrayCreation);
        if (typeInfo.Type is not IArrayTypeSymbol arrayType)
            return;

        var elementType = arrayType.ElementType.SpecialType;
        if (elementType != SpecialType.System_Byte && elementType != SpecialType.System_Char)
            return;

        // Must have a single rank specifier with a constant size
        if (arrayCreation.Type.RankSpecifiers.Count != 1)
            return;

        var rankSpecifier = arrayCreation.Type.RankSpecifiers[0];
        if (rankSpecifier.Sizes.Count != 1)
            return;

        var sizeExpr = rankSpecifier.Sizes[0];
        var constantValue = context.SemanticModel.GetConstantValue(sizeExpr);
        if (!constantValue.HasValue || constantValue.Value is not int size)
            return;

        if (size > MaxStackallocSize || size <= 0)
            return;

        // Must be in a local variable declaration (not a field, not returned, not passed as argument)
        if (arrayCreation.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } } })
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, arrayCreation.GetLocation()));
    }
}
