using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseFrozenSetAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseFrozenSet,
        "Use FrozenSet for read-only set",
        "HashSet '{0}' is never mutated after initialization — consider using FrozenSet",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    private static readonly ImmutableHashSet<string> MutatingMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Add", "Remove", "Clear", "UnionWith", "IntersectWith", "ExceptWith", "SymmetricExceptWith");

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            if (!TfmHelper.TryGetTfm(compilationContext.Options, out var tfm) || !TfmHelper.IsNet8OrLater(tfm))
                return;

            compilationContext.RegisterSymbolAction(AnalyzeField, SymbolKind.Field);
        });
    }

    private static void AnalyzeField(SymbolAnalysisContext context)
    {
        var field = (IFieldSymbol)context.Symbol;

        if (!field.IsReadOnly) return;

        if (field.Type is not INamedTypeSymbol namedType) return;
        if (namedType.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.HashSet<T>")
            return;

        var containingType = field.ContainingType;
        foreach (var syntaxRef in containingType.DeclaringSyntaxReferences)
        {
            var typeSyntax = syntaxRef.GetSyntax(context.CancellationToken);

            foreach (var invocation in typeSyntax.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
                    && MutatingMethods.Contains(memberAccess.Name.Identifier.Text)
                    && IsFieldReference(memberAccess.Expression, field.Name))
                {
                    var enclosingCtor = invocation.FirstAncestorOrSelf<ConstructorDeclarationSyntax>();
                    if (enclosingCtor == null)
                        return;
                }
            }
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, field.Locations[0], field.Name));
    }

    private static bool IsFieldReference(ExpressionSyntax expression, string fieldName)
    {
        return expression switch
        {
            IdentifierNameSyntax id => id.Identifier.Text == fieldName,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax, Name: IdentifierNameSyntax name }
                => name.Identifier.Text == fieldName,
            _ => false
        };
    }
}
