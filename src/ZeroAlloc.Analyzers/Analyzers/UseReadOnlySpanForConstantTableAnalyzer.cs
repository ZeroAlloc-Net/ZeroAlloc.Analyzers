using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseReadOnlySpanForConstantTableAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseReadOnlySpanForConstantTable,
        "Use a ReadOnlySpan<T> property for constant lookup tables",
        "Lookup table '{0}' can be a 'static ReadOnlySpan<{1}>' property that reads constant data without allocating",
        DiagnosticCategories.Memory,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var readOnlySpan = start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            if (readOnlySpan is null)
                return;

            // .NET 7+: constant ReadOnlySpan<T> data of wider primitives is read through RuntimeHelpers.CreateSpan.
            var hasCreateSpan = start.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.RuntimeHelpers")
                ?.GetMembers("CreateSpan").Length > 0;

            var candidates = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
            var disqualified = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterOperationAction(context =>
            {
                var initializer = (IFieldInitializerOperation)context.Operation;
                if (initializer.InitializedFields.Length == 1
                    && IsCandidateField(initializer.InitializedFields[0], hasCreateSpan)
                    && IsConstantArray(initializer.Value))
                {
                    candidates[initializer.InitializedFields[0]] = true;
                }
            }, OperationKind.FieldInitializer);

            start.RegisterOperationAction(context =>
            {
                var reference = (IFieldReferenceOperation)context.Operation;
                if (reference.Field.IsStatic && reference.Field.Type is IArrayTypeSymbol
                    && !ReadOnlySpanUse.IsCompatible(reference, readOnlySpan))
                {
                    disqualified[reference.Field.OriginalDefinition] = true;
                }
            }, OperationKind.FieldReference);

            start.RegisterCompilationEndAction(context =>
            {
                foreach (var field in candidates.Keys)
                {
                    if (disqualified.ContainsKey(field))
                        continue;

                    var elementType = ((IArrayTypeSymbol)field.Type).ElementType;
                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        field.Locations[0],
                        field.Name,
                        elementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
                }
            });
        });
    }

    private static bool IsCandidateField(IFieldSymbol field, bool hasCreateSpan)
    {
        if (!field.IsStatic || !field.IsReadOnly || field.IsConst
            || field.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Internal)
            || field.Type is not IArrayTypeSymbol { Rank: 1 } array
            || field.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        if (field.DeclaringSyntaxReferences[0].GetSyntax() is
            Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax
            {
                Parent: Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclarationSyntax { Variables.Count: > 1 }
            })
        {
            return false;
        }

        if (!field.GetAttributes().IsEmpty)
            return false;

        return array.ElementType.SpecialType switch
        {
            SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => true,
            SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16
                or SpecialType.System_Int32 or SpecialType.System_UInt32
                or SpecialType.System_Int64 or SpecialType.System_UInt64
                or SpecialType.System_Single or SpecialType.System_Double => hasCreateSpan,
            _ => false,
        };
    }

    private static bool IsConstantArray(IOperation value)
    {
        while (value is IConversionOperation { IsImplicit: true } conversion)
            value = conversion.Operand;

        var elements = value switch
        {
            IArrayCreationOperation { Initializer: { } initializer } => initializer.ElementValues,
            ICollectionExpressionOperation collection => collection.Elements,
            _ => default,
        };

        return !elements.IsDefaultOrEmpty && elements.All(element => element.ConstantValue.HasValue);
    }
}
