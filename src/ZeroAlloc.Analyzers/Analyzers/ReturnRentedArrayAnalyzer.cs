using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReturnRentedArrayAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ReturnRentedArray,
        "Return rented arrays to the pool",
        "Array rented into '{0}' is never returned to the pool",
        DiagnosticCategories.Memory,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    // Methods on these types read or write a passed array without taking ownership of it,
    // provided the call does not hand back a value that can keep the array alive.
    // A returned span keeps the array alive but cannot hand ownership back, so it is no escape.
    private static readonly string[] TrustedTypeNames =
    [
        "System.MemoryExtensions",
        "System.Array",
        "System.Buffer",
        "System.IO.Stream",
    ];

    // Stack-only views, which cannot carry ownership back to the pool even when returned.
    // Memory<T> and ReadOnlyMemory<T> are heap-storable and can, so they are not listed.
    private static readonly string[] SpanTypeNames =
    [
        "System.Span`1",
        "System.ReadOnlySpan`1",
    ];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            var arrayPool = start.Compilation.GetTypeByMetadataName("System.Buffers.ArrayPool`1");
            if (arrayPool is null)
                return;

            var known = new KnownTypes(
                arrayPool,
                Resolve(start.Compilation, TrustedTypeNames),
                Resolve(start.Compilation, SpanTypeNames));

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

    private static void AnalyzeInvocation(OperationAnalysisContext context, KnownTypes known)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (invocation.TargetMethod.Name != "Rent" || !known.IsArrayPool(invocation.TargetMethod.ContainingType))
            return;

        if (GetAssignedLocal(invocation) is not { } local)
            return;

        IOperation root = invocation;
        while (root.Parent is not null)
            root = root.Parent;

        var rentFunctions = EnclosingFunctions(invocation);

        foreach (var reference in root.Descendants().OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Local, local))
                continue;

            // Writing the local is not a use of the rented array.
            if (reference.Parent is ISimpleAssignmentOperation assignment && assignment.Target == reference)
                continue;

            if (IsReturnedOrEscapes(reference, rentFunctions, known))
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), local.Name));
    }

    private static ILocalSymbol? GetAssignedLocal(IInvocationOperation invocation) => invocation.Parent switch
    {
        IVariableInitializerOperation { Parent: IVariableDeclaratorOperation declarator } => declarator.Symbol,
        ISimpleAssignmentOperation { Target: ILocalReferenceOperation target } assignment
            when assignment.Value == invocation => target.Local,
        _ => null,
    };

    private static ImmutableArray<IOperation> EnclosingFunctions(IOperation operation)
    {
        var builder = ImmutableArray.CreateBuilder<IOperation>();
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                builder.Add(current);
        }

        return builder.ToImmutable();
    }

    // True when this use hands the array back to the pool, or lets it leave the method so
    // ownership may have moved. Either way the rule must stay silent.
    private static bool IsReturnedOrEscapes(
        ILocalReferenceOperation reference,
        ImmutableArray<IOperation> rentFunctions,
        KnownTypes known)
    {
        for (var current = reference.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation && !rentFunctions.Contains(current))
                return true;
        }

        switch (reference.Parent)
        {
            case IArgumentOperation { Parent: IInvocationOperation call } argument:
                if (call.TargetMethod.Name == "Return" && known.IsArrayPool(call.TargetMethod.ContainingType))
                    return true;
                return argument.Parameter?.RefKind != RefKind.None
                    || !known.IsTrusted(call.TargetMethod.ContainingType)
                    || !IsNonRetainingCall(call, known);
            case IArgumentOperation { Parent: IObjectCreationOperation creation }:
                return !known.IsSpan(creation.Type);
            case IConversionOperation conversion:
                if (conversion.Parent is IForEachLoopOperation)
                    return false;
                return !known.IsSpan(conversion.Type);
            case IArrayElementReferenceOperation element:
                return element.ArrayReference != reference;
            case IPropertyReferenceOperation property:
                return property.Instance != reference;
            case IInvocationOperation call:
                return call.Instance != reference;
            case IForEachLoopOperation:
                return false;
            default:
                return true;
        }
    }

    // A trusted call gives the pool no way back to its array when it returns void, a primitive or
    // other special non-object type, or a span. Object is excluded because it can hold the array;
    // Memory, arrays, tasks and collections can too.
    private static bool IsNonRetainingCall(IInvocationOperation call, KnownTypes known)
    {
        var returnType = call.TargetMethod.ReturnType;
        if (returnType.SpecialType is not (SpecialType.None or SpecialType.System_Object))
            return true;

        return known.IsSpan(returnType);
    }

    private sealed class KnownTypes(
        INamedTypeSymbol arrayPool,
        ImmutableArray<INamedTypeSymbol> trustedTypes,
        ImmutableArray<INamedTypeSymbol> spanTypes)
    {
        public bool IsArrayPool(ITypeSymbol? type) => InheritsFrom(type, arrayPool);

        public bool IsTrusted(ITypeSymbol? type) => trustedTypes.Any(trusted => InheritsFrom(type, trusted));

        public bool IsSpan(ITypeSymbol? type) =>
            type is INamedTypeSymbol named && spanTypes.Contains(named.OriginalDefinition, SymbolEqualityComparer.Default);

        private static bool InheritsFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
        {
            for (var current = type; current is not null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
                    return true;
            }

            return false;
        }
    }
}
