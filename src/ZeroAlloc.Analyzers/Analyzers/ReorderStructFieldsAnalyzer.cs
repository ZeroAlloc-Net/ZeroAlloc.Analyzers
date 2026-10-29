using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Flags structs whose field declaration order wastes bytes to alignment padding.
/// Reordering fields from largest alignment to smallest shrinks the struct, which
/// reduces its memory footprint and how many cache lines it spans when stored in
/// arrays or collections.
///
/// Only structs with the compiler-default layout (no explicit
/// <see cref="System.Runtime.InteropServices.StructLayoutAttribute"/>) are considered, and
/// only when the layout of every field is known: primitives, enums, pointers, fixed
/// buffers, <see cref="System.Nullable{T}"/>, a small table of BCL structs, and source
/// structs that meet the same conditions themselves. Anything else, a reference field
/// above all, makes the runtime choose the layout, so the rule stays silent.
/// Pointer-sized fields are measured for a 64-bit target.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReorderStructFieldsAnalyzer : DiagnosticAnalyzer
{
    private const int PointerSize = 8;

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.ReorderStructFields,
        "Reorder struct fields to reduce padding",
        "Struct '{0}' wastes {1} byte(s) to field padding — reordering fields (largest alignment first) shrinks it from {2} to {3} bytes",
        DiagnosticCategories.DataLayout,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            // DateTime, DateTimeOffset and decimal keep declaration order and the sizes below
            // only on modern .NET; on .NET Framework they differ, so we bail there.
            var modernNet = TfmHelper.TryGetTfm(compilationContext.Options, out var tfm)
                && TfmHelper.IsNet8OrLater(tfm);

            compilationContext.RegisterSymbolAction(
                symbolContext => AnalyzeType(symbolContext, modernNet),
                SymbolKind.NamedType);
        });
    }

    private static void AnalyzeType(SymbolAnalysisContext context, bool modernNet)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind != TypeKind.Struct)
            return;

        // Only analyze types written in source.
        if (type.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            return;

        // An explicit [StructLayout] signals intentional ordering (interop/serialization);
        // never suggest reordering those. Compiler-default structs are safe to reorder.
        // An [InlineArray] struct has one field repeated, so there is nothing to reorder.
        if (HasExplicitStructLayout(type) || IsInlineArray(type))
            return;

        var visiting = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default) { type };
        if (!TryGetFieldLayouts(type, modernNet, visiting, out var layouts) || layouts.Count < 2)
            return;

        int currentSize = ComputeSize(layouts);

        // Optimal packing: place fields with the largest alignment first.
        var optimalOrder = layouts.OrderByDescending(s => s.Align).ThenByDescending(s => s.Size).ToList();
        int optimalSize = ComputeSize(optimalOrder);

        if (optimalSize >= currentSize)
            return;

        var location = type.Locations.FirstOrDefault();
        if (location == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, location, type.Name, currentSize - optimalSize, currentSize, optimalSize));
    }

    /// <summary>
    /// Gets the size and alignment of every instance field of a source struct, in
    /// declaration order. Fails when any field's layout is unknown.
    /// </summary>
    private static bool TryGetFieldLayouts(
        INamedTypeSymbol type,
        bool modernNet,
        HashSet<ITypeSymbol> visiting,
        out List<(int Size, int Align)> layouts)
    {
        layouts = [];

        // A captured primary constructor parameter becomes a field that is not a member.
        if (HasPrimaryConstructor(type))
            return false;

        foreach (var member in type.GetMembers())
        {
            if (member.IsStatic)
                continue;

            // A field-like event has a hidden delegate field, so the runtime picks the layout.
            if (member is IEventSymbol { AddMethod.IsImplicitlyDeclared: true })
                return false;

            // Auto-property backing fields are members too, laid out where the property is.
            if (member is not IFieldSymbol field || field.IsConst)
                continue;

            if (!TryGetFieldLayout(field, modernNet, visiting, out var size, out var align))
                return false;

            layouts.Add((size, align));
        }

        return true;
    }

    private static bool TryGetFieldLayout(
        IFieldSymbol field,
        bool modernNet,
        HashSet<ITypeSymbol> visiting,
        out int size,
        out int align)
    {
        if (field.IsFixedSizeBuffer && field.Type is IPointerTypeSymbol buffer)
        {
            if (!TryGetTypeLayout(buffer.PointedAtType, modernNet, visiting, out size, out align))
                return false;

            size *= field.FixedSize;
            return true;
        }

        return TryGetTypeLayout(field.Type, modernNet, visiting, out size, out align);
    }

    private static bool TryGetTypeLayout(
        ITypeSymbol type,
        bool modernNet,
        HashSet<ITypeSymbol> visiting,
        out int size,
        out int align)
    {
        size = align = 0;

        if (type is IPointerTypeSymbol || type is IFunctionPointerTypeSymbol)
        {
            size = align = PointerSize;
            return true;
        }

        // Treat an enum as its underlying primitive.
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol named && named.EnumUnderlyingType != null)
            type = named.EnumUnderlyingType;

        if (TryGetPrimitiveLayout(type, modernNet, out size, out align))
            return true;

        if (type is not INamedTypeSymbol namedType || namedType.TypeKind != TypeKind.Struct)
            return false; // reference types, type parameters, error types

        // Nullable<T> is laid out as { bool hasValue; T value; }.
        if (namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
        {
            if (!TryGetTypeLayout(namedType.TypeArguments[0], modernNet, visiting, out var valueSize, out var valueAlign))
                return false;

            List<(int Size, int Align)> nullable = [(1, 1), (valueSize, valueAlign)];
            size = ComputeSize(nullable);
            align = Math.Max(1, valueAlign);
            return true;
        }

        if (TryGetKnownStructLayout(namedType, modernNet, out size, out align))
            return true;

        // Any other struct: only one declared in source, so we can see all its fields.
        if (namedType.DeclaringSyntaxReferences.IsDefaultOrEmpty || HasExplicitStructLayout(namedType))
            return false;

        if (!visiting.Add(namedType))
            return false; // a layout cycle is a compile error; don't recurse forever

        try
        {
            if (IsInlineArray(namedType, out var length))
            {
                if (!TryGetFieldLayouts(namedType, modernNet, visiting, out var element) || element.Count != 1)
                    return false;

                size = element[0].Size * length;
                align = element[0].Align;
                return true;
            }

            if (!TryGetFieldLayouts(namedType, modernNet, visiting, out var fields))
                return false;

            if (fields.Count == 0)
            {
                size = align = 1; // an empty struct still occupies one byte
                return true;
            }

            size = ComputeSize(fields);
            align = fields.Max(f => f.Align);
            return true;
        }
        finally
        {
            visiting.Remove(namedType);
        }
    }

    private static int ComputeSize(List<(int Size, int Align)> fields)
    {
        int offset = 0;
        int maxAlign = 1;

        foreach (var (size, align) in fields)
        {
            offset = AlignUp(offset, align);
            offset += size;
            if (align > maxAlign)
                maxAlign = align;
        }

        // The struct's own size is rounded up to its largest field alignment.
        return AlignUp(offset, maxAlign);
    }

    private static int AlignUp(int value, int alignment)
        => (value + alignment - 1) / alignment * alignment;

    private static bool HasExplicitStructLayout(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString()
                == "System.Runtime.InteropServices.StructLayoutAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInlineArray(INamedTypeSymbol type) => IsInlineArray(type, out _);

    private static bool IsInlineArray(INamedTypeSymbol type, out int length)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString()
                == "System.Runtime.CompilerServices.InlineArrayAttribute")
            {
                length = attribute.ConstructorArguments.Length == 1
                    && attribute.ConstructorArguments[0].Value is int value
                    && value > 0
                        ? value
                        : 0;
                return length > 0;
            }
        }

        length = 0;
        return false;
    }

    private static bool HasPrimaryConstructor(INamedTypeSymbol type)
    {
        foreach (var reference in type.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax { ParameterList: not null } declaration
                && declaration is not RecordDeclarationSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetKnownStructLayout(INamedTypeSymbol type, bool modernNet, out int size, out int align)
    {
        // BCL structs whose layout we measured. Other metadata structs may hide private
        // fields or use auto layout, so they are not guessed at.
        switch (type.ToDisplayString())
        {
            case "System.Guid":
                size = 16;
                align = 4;
                return true;
            case "System.TimeSpan":
                size = align = 8;
                return true;
            case "System.DateTime" when modernNet:
                size = align = 8;
                return true;
            case "System.DateTimeOffset" when modernNet:
                size = 16;
                align = 8;
                return true;
            default:
                size = align = 0;
                return false;
        }
    }

    private static bool TryGetPrimitiveLayout(ITypeSymbol type, bool modernNet, out int size, out int align)
    {
        switch (type.SpecialType)
        {
            case SpecialType.System_Boolean:
            case SpecialType.System_Byte:
            case SpecialType.System_SByte:
                size = align = 1;
                return true;
            case SpecialType.System_Char:
            case SpecialType.System_Int16:
            case SpecialType.System_UInt16:
                size = align = 2;
                return true;
            case SpecialType.System_Int32:
            case SpecialType.System_UInt32:
            case SpecialType.System_Single:
                size = align = 4;
                return true;
            case SpecialType.System_Int64:
            case SpecialType.System_UInt64:
            case SpecialType.System_Double:
                size = align = 8;
                return true;
            case SpecialType.System_IntPtr:
            case SpecialType.System_UIntPtr:
                size = align = PointerSize;
                return true;
            case SpecialType.System_Decimal when modernNet:
                // { int flags; uint hi32; ulong lo64; } on .NET; four ints on .NET Framework.
                size = 16;
                align = 8;
                return true;
            default:
                size = align = 0;
                return false;
        }
    }
}
