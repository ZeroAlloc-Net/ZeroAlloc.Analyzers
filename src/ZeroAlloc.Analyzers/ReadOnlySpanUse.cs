using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

internal static class ReadOnlySpanUse
{
    /// <summary>
    /// True when this use of an array would keep compiling with the same meaning if the array
    /// became a <c>ReadOnlySpan&lt;T&gt;</c>: element reads, <c>Length</c>, <c>foreach</c>, and
    /// implicit conversion to <c>ReadOnlySpan&lt;T&gt;</c>.
    /// </summary>
    public static bool IsCompatible(IOperation arrayReference, INamedTypeSymbol readOnlySpan)
    {
        switch (arrayReference.Parent)
        {
            case IArrayElementReferenceOperation element when element.ArrayReference == arrayReference:
                return element.Indices.All(index => index.Type?.SpecialType == SpecialType.System_Int32)
                    && !IsWrittenOrMutated(element);
            case IPropertyReferenceOperation { Property.Name: "Length" } length when length.Instance == arrayReference:
                return true;
            case IForEachLoopOperation loop:
                return loop.Collection == arrayReference;
            case IConversionOperation { IsImplicit: true } conversion:
                return conversion.Parent is IForEachLoopOperation conversionLoop && conversionLoop.Collection == conversion
                    || conversion.Type is INamedTypeSymbol target
                        && SymbolEqualityComparer.Default.Equals(target.OriginalDefinition, readOnlySpan);
            default:
                return false;
        }
    }

    // A ReadOnlySpan<T> element is readonly, so writes, ref access and mutating calls on
    // mutable struct elements stop compiling or start working on a defensive copy.
    private static bool IsWrittenOrMutated(IArrayElementReferenceOperation element)
    {
        if (element.Syntax.Parent is RefExpressionSyntax)
            return true;

        switch (element.Parent)
        {
            case ISimpleAssignmentOperation assignment when assignment.Target == element:
            case ICompoundAssignmentOperation compound when compound.Target == element:
            case ICoalesceAssignmentOperation coalesce when coalesce.Target == element:
            case IIncrementOrDecrementOperation:
            case IAddressOfOperation:
            case IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out }:
                return true;
        }

        // A type parameter without a reference type constraint may be a mutable struct.
        var mayBeMutableStruct = element.Type is ITypeParameterSymbol { IsReferenceType: false }
            || element.Type is { IsValueType: true, IsReadOnly: false, SpecialType: SpecialType.None, TypeKind: not TypeKind.Enum };

        return mayBeMutableStruct && element.Parent is IMemberReferenceOperation or IInvocationOperation;
    }
}
