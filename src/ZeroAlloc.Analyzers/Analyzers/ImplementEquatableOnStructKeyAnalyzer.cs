using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ImplementEquatableOnStructKeyAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ImplementEquatableOnStructKey,
        "Implement IEquatable<T> on structs used as hash keys",
        "Struct '{0}' is used as a hash key but does not implement IEquatable<{0}>, so every lookup boxes it",
        DiagnosticCategories.ValueTypes,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    // Hashed collections whose first type argument is the key.
    private static readonly string[] CollectionTypeNames =
    [
        "System.Collections.Generic.Dictionary`2",
        "System.Collections.Generic.HashSet`1",
        "System.Collections.Concurrent.ConcurrentDictionary`2",
    ];

    // Static factories that build a hashed collection from a sequence.
    private static readonly string[] FactoryTypeNames =
    [
        "System.Linq.Enumerable",
        "System.Collections.Frozen.FrozenDictionary",
        "System.Collections.Frozen.FrozenSet",
    ];

    private static readonly string[] FactoryMethodNames = ["ToDictionary", "ToHashSet", "ToFrozenDictionary", "ToFrozenSet"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var equatable = start.Compilation.GetTypeByMetadataName("System.IEquatable`1");
            var comparer = start.Compilation.GetTypeByMetadataName("System.Collections.Generic.IEqualityComparer`1");
            if (equatable is null || comparer is null)
                return;

            var known = new KnownTypes(
                start.Compilation.Assembly,
                equatable,
                comparer,
                Resolve(start.Compilation, CollectionTypeNames),
                Resolve(start.Compilation, FactoryTypeNames));

            start.RegisterOperationAction(context => AnalyzeObjectCreation(context, known), OperationKind.ObjectCreation);
            start.RegisterOperationAction(context => AnalyzeInvocation(context, known), OperationKind.Invocation);
        });
    }

    private static ImmutableArray<INamedTypeSymbol> Resolve(Compilation compilation, string[] metadataNames)
    {
        var builder = ImmutableArray.CreateBuilder<INamedTypeSymbol>();
        foreach (var name in metadataNames)
        {
            if (compilation.GetTypeByMetadataName(name) is { } type)
                builder.Add(type);
        }

        return builder.ToImmutable();
    }

    private static void AnalyzeObjectCreation(OperationAnalysisContext context, KnownTypes known)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        if (creation.Type is not INamedTypeSymbol { IsGenericType: true } type
            || !known.Collections.Contains(type.OriginalDefinition, SymbolEqualityComparer.Default)
            || known.PassesComparer(creation.Arguments))
        {
            return;
        }

        Report(context, known, type.TypeArguments[0], creation.Syntax);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes known)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (!method.IsGenericMethod
            || Array.IndexOf(FactoryMethodNames, method.Name) < 0
            || !known.Factories.Contains(method.ContainingType.OriginalDefinition, SymbolEqualityComparer.Default)
            || known.PassesComparer(invocation.Arguments))
        {
            return;
        }

        // ToDictionary and ToFrozenDictionary name their key TKey; ToHashSet and ToFrozenSet key on their only type argument.
        var keyIndex = 0;
        for (var i = 0; i < method.TypeParameters.Length; i++)
        {
            if (method.TypeParameters[i].Name == "TKey")
                keyIndex = i;
        }

        Report(context, known, method.TypeArguments[keyIndex], invocation.Syntax);
    }

    private static void Report(OperationAnalysisContext context, KnownTypes known, ITypeSymbol key, SyntaxNode syntax)
    {
        // EqualityComparer<T>.Default boxes Nullable<S> keys too when S lacks IEquatable<S>.
        if (key is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
            key = nullable.TypeArguments[0];

        if (key is not INamedTypeSymbol { TypeKind: TypeKind.Struct, IsRecord: false } structType
            || !SymbolEqualityComparer.Default.Equals(structType.ContainingAssembly, known.Assembly)
            || known.ImplementsEquatable(structType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Rule,
            syntax.GetLocation(),
            structType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)));
    }

    private sealed class KnownTypes(
        IAssemblySymbol assembly,
        INamedTypeSymbol equatable,
        INamedTypeSymbol comparer,
        ImmutableArray<INamedTypeSymbol> collections,
        ImmutableArray<INamedTypeSymbol> factories)
    {
        public IAssemblySymbol Assembly { get; } = assembly;

        public ImmutableArray<INamedTypeSymbol> Collections { get; } = collections;

        public ImmutableArray<INamedTypeSymbol> Factories { get; } = factories;

        // ToFrozenSet and ToFrozenDictionary take an optional comparer, so the parameter alone proves
        // nothing: a comparer counts only when an argument actually supplies a non-null one.
        public bool PassesComparer(ImmutableArray<IArgumentOperation> arguments) =>
            arguments.Any(a => a.ArgumentKind != ArgumentKind.DefaultValue
                && a.Parameter?.Type is INamedTypeSymbol named
                && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, comparer)
                && a.Value.ConstantValue is not { HasValue: true, Value: null });

        public bool ImplementsEquatable(INamedTypeSymbol type) =>
            type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i.OriginalDefinition, equatable)
                && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], type));
    }
}
