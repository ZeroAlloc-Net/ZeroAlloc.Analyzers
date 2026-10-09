using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidConcurrentDictionarySnapshotAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidConcurrentDictionarySnapshot,
        "Enumerate the ConcurrentDictionary instead of its Keys or Values",
        "'{0}' copies the dictionary under all locks; enumerate the dictionary and use '.{1}' instead",
        DiagnosticCategories.Collections,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var concurrentDictionary = start.Compilation.GetTypeByMetadataName("System.Collections.Concurrent.ConcurrentDictionary`2");
            if (concurrentDictionary is null)
                return;

            var enumerable = start.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            start.RegisterOperationAction(
                context => AnalyzePropertyReference(context, concurrentDictionary, enumerable),
                OperationKind.PropertyReference);
        });
    }

    private static void AnalyzePropertyReference(
        OperationAnalysisContext context,
        INamedTypeSymbol concurrentDictionary,
        INamedTypeSymbol? enumerable)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        if (reference.Property.Name is not ("Keys" or "Values")
            || !SymbolEqualityComparer.Default.Equals(reference.Property.ContainingType.OriginalDefinition, concurrentDictionary))
        {
            return;
        }

        IOperation use = reference;
        while (use.Parent is IConversionOperation { IsImplicit: true } conversion)
            use = conversion;

        var enumerated = use.Parent switch
        {
            IForEachLoopOperation loop => loop.Collection == use,
            IArgumentOperation { Parent: IInvocationOperation call } argument =>
                enumerable is not null
                && call.TargetMethod.IsExtensionMethod
                && SymbolEqualityComparer.Default.Equals(call.TargetMethod.ContainingType, enumerable)
                && argument.Parameter?.Ordinal == 0,
            _ => false,
        };

        if (!enumerated)
            return;

        var member = reference.Property.Name == "Keys" ? "Key" : "Value";
        context.ReportDiagnostic(Diagnostic.Create(Rule, reference.Syntax.GetLocation(), reference.Syntax.ToString(), member));
    }
}
