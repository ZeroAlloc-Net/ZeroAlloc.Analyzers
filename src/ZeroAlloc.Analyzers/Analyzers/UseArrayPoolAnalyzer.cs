using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseArrayPoolAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseArrayPool,
        "Use ArrayPool for large temporary arrays",
        "Large array allocation in method scope — consider using ArrayPool<T>.Shared.Rent()",
        DiagnosticCategories.Memory,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    private const int MinArrayPoolSize = 257;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeArrayCreation, SyntaxKind.ArrayCreationExpression);
    }

    private static void AnalyzeArrayCreation(SyntaxNodeAnalysisContext context)
    {
        var arrayCreation = (ArrayCreationExpressionSyntax)context.Node;

        // Must be byte[]
        var typeInfo = context.SemanticModel.GetTypeInfo(arrayCreation);
        if (typeInfo.Type is not IArrayTypeSymbol arrayType)
            return;

        if (arrayType.ElementType.SpecialType != SpecialType.System_Byte)
            return;

        // Must have a single rank specifier
        if (arrayCreation.Type.RankSpecifiers.Count != 1)
            return;

        var rankSpecifier = arrayCreation.Type.RankSpecifiers[0];
        if (rankSpecifier.Sizes.Count != 1)
            return;

        var sizeExpr = rankSpecifier.Sizes[0];

        // Check if constant and large
        var constantValue = context.SemanticModel.GetConstantValue(sizeExpr);
        bool isLarge;
        if (constantValue.HasValue && constantValue.Value is int size)
        {
            isLarge = size >= MinArrayPoolSize;
        }
        else
        {
            // Non-constant size — likely a variable, suggest ArrayPool
            isLarge = true;
        }

        if (!isLarge)
            return;

        // Must be in a local variable declaration
        if (arrayCreation.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax } } })
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, arrayCreation.GetLocation()));
    }
}
