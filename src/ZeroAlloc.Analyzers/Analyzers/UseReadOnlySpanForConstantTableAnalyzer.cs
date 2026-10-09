using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var readOnlySpan = start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            if (readOnlySpan is null)
                return;

            // .NET 7+: constant ReadOnlySpan<T> data of wider primitives is read through RuntimeHelpers.CreateSpan.
            var hasCreateSpan = start.Compilation.GetTypeByMetadataName("System.Runtime.CompilerServices.RuntimeHelpers")
                ?.GetMembers("CreateSpan").Length > 0;

            // Friend assemblies can read internal fields, where this rule cannot see the use.
            var hasFriendAssemblies = start.Compilation.Assembly.GetAttributes().Any(a =>
                a.AttributeClass is { Name: "InternalsVisibleToAttribute" } attribute
                && attribute.ContainingNamespace.ToDisplayString() == "System.Runtime.CompilerServices");

            var expressionTreeBase = start.Compilation.GetTypeByMetadataName("System.Linq.Expressions.LambdaExpression");

            var candidates = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);
            var disqualified = new ConcurrentDictionary<IFieldSymbol, bool>(SymbolEqualityComparer.Default);

            start.RegisterOperationAction(context =>
            {
                var initializer = (IFieldInitializerOperation)context.Operation;
                if (initializer.InitializedFields.Length == 1
                    && IsCandidateField(initializer.InitializedFields[0], hasCreateSpan, hasFriendAssemblies)
                    && !GeneratedCode.IsGenerated(initializer.Syntax.SyntaxTree)
                    && IsConstantArray(initializer.Value))
                {
                    candidates[initializer.InitializedFields[0]] = true;
                }
            }, OperationKind.FieldInitializer);

            start.RegisterOperationAction(context =>
            {
                var reference = (IFieldReferenceOperation)context.Operation;
                // Check the cheap candidacy first; most static array fields are never candidates.
                if (IsCandidateField(reference.Field.OriginalDefinition, hasCreateSpan, hasFriendAssemblies)
                    && (!ReadOnlySpanUse.IsCompatible(reference, readOnlySpan)
                        || AsyncOrIterator.IsInAsyncOrIterator(reference, context.ContainingSymbol)
                        || IsInExpressionTree(reference, expressionTreeBase)))
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

    private static bool IsCandidateField(IFieldSymbol field, bool hasCreateSpan, bool hasFriendAssemblies)
    {
        if (!field.IsStatic || !field.IsReadOnly || field.IsConst
            || field.DeclaredAccessibility is not (Accessibility.Private or Accessibility.Internal)
            || field.DeclaredAccessibility == Accessibility.Internal && hasFriendAssemblies
            || field.Type is not IArrayTypeSymbol { Rank: 1 } array
            || field.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        if (field.DeclaringSyntaxReferences[0].GetSyntax() is VariableDeclaratorSyntax
            {
                Parent: VariableDeclarationSyntax { Variables.Count: > 1 }
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

    // An expression tree cannot call a property that returns a ReadOnlySpan<T>.
    private static bool IsInExpressionTree(IOperation reference, INamedTypeSymbol? lambdaExpression)
    {
        if (lambdaExpression is null)
            return false;

        for (var current = reference.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation { Parent: IConversionOperation { Type: { } type } })
            {
                for (var baseType = type; baseType is not null; baseType = baseType.BaseType)
                {
                    if (SymbolEqualityComparer.Default.Equals(baseType, lambdaExpression))
                        return true;
                }
            }
        }

        return false;
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
