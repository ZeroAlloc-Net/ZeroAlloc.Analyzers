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
    private static readonly string[] TrustedTypeNames =
    [
        "System.MemoryExtensions",
        "System.Array",
        "System.Buffer",
        "System.IO.Stream",
    ];

    // Stack-only views. Memory<T> and ReadOnlyMemory<T> are heap-storable, so they are not listed.
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
        var returnsSpan = ReturnsSpanByValue(invocation, context.ContainingSymbol, known);

        foreach (var reference in root.Descendants().OfType<ILocalReferenceOperation>())
        {
            if (!SymbolEqualityComparer.Default.Equals(reference.Local, local))
                continue;

            // Writing the local is not a use of the rented array.
            if (reference.Parent is ISimpleAssignmentOperation assignment && assignment.Target == reference)
                continue;

            if (IsReturnedOrEscapes(reference, rentFunctions, returnsSpan, known))
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
        bool returnsSpan,
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
                    || !IsNonRetainingCall(call, returnsSpan, known);
            case IArgumentOperation { Parent: IObjectCreationOperation creation }:
                return !known.IsSpan(creation.Type) || returnsSpan || IsDirectlyReturned(creation);
            case IConversionOperation conversion:
                if (conversion.Parent is IForEachLoopOperation)
                    return false;
                return !known.IsSpan(conversion.Type) || returnsSpan || IsDirectlyReturned(conversion);
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

    // A trusted call keeps no reference to its array when it returns void, a primitive or other
    // special non-object type, or a span that the caller does not get back directly. Object is
    // excluded because it can hold the array; Memory, arrays, tasks and collections can too.
    private static bool IsNonRetainingCall(IInvocationOperation call, bool returnsSpan, KnownTypes known)
    {
        var returnType = call.TargetMethod.ReturnType;
        if (returnType.SpecialType is not (SpecialType.None or SpecialType.System_Object))
            return true;

        return known.IsSpan(returnType) && !returnsSpan && !IsDirectlyReturned(call);
    }

    // True when the function that owns the rent (the innermost lambda or local function around it,
    // else the analyzed method) returns a span by value. A span handed out there leaves the method.
    private static bool ReturnsSpanByValue(IOperation rent, ISymbol? containingSymbol, KnownTypes known)
    {
        for (var current = rent.Parent; current is not null; current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation lambda)
                return known.IsSpan(lambda.Symbol.ReturnType);

            if (current is ILocalFunctionOperation localFunction)
                return known.IsSpan(localFunction.Symbol.ReturnType);
        }

        return containingSymbol is IMethodSymbol method && known.IsSpan(method.ReturnType);
    }

    // True when the value, possibly after implicit conversions, is the operand of a return.
    private static bool IsDirectlyReturned(IOperation operation)
    {
        var current = operation;
        while (current.Parent is IConversionOperation conversion)
            current = conversion;

        return current.Parent is IReturnOperation;
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
