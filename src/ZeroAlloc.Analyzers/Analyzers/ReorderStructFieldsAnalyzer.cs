using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Flags structs whose field declaration order wastes bytes to alignment padding.
/// Reordering fields from largest alignment to smallest shrinks the struct, which
/// reduces its memory footprint and how many cache lines it spans when stored in
/// arrays or collections.
///
/// Only structs with the compiler-default layout (no explicit
/// <see cref="System.Runtime.InteropServices.StructLayoutAttribute"/>) and made up
/// exclusively of fixed-size primitive/enum fields are considered, so the suggested
/// reorder is always safe and the size computation is deterministic across platforms.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ReorderStructFieldsAnalyzer : DiagnosticAnalyzer
{
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
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;

        if (type.TypeKind != TypeKind.Struct)
            return;

        // Only analyze types written in source.
        if (type.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            return;

        // An explicit [StructLayout] signals intentional ordering (interop/serialization);
        // never suggest reordering those. Compiler-default structs are safe to reorder.
        if (HasExplicitStructLayout(type))
            return;

        var fields = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared)
            .ToList();

        if (fields.Count < 2)
            return;

        var sizes = new List<(int Size, int Align)>(fields.Count);
        foreach (var fieldSymbol in fields)
        {
            if (!TryGetPrimitiveLayout(fieldSymbol.Type, out var size, out var align))
                return; // unknown/managed field type — bail to stay deterministic and safe

            sizes.Add((size, align));
        }

        int currentSize = ComputeSize(sizes);

        // Optimal packing: place fields with the largest alignment first.
        var optimalOrder = sizes.OrderByDescending(s => s.Align).ThenByDescending(s => s.Size).ToList();
        int optimalSize = ComputeSize(optimalOrder);

        if (optimalSize >= currentSize)
            return;

        var location = type.Locations.FirstOrDefault();
        if (location == null)
            return;

        context.ReportDiagnostic(Diagnostic.Create(
            Rule, location, type.Name, currentSize - optimalSize, currentSize, optimalSize));
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

    private static bool TryGetPrimitiveLayout(ITypeSymbol type, out int size, out int align)
    {
        // Treat an enum as its underlying primitive.
        if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol named && named.EnumUnderlyingType != null)
            type = named.EnumUnderlyingType;

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
            default:
                // Decimal, pointers, IntPtr/UIntPtr, nested structs and reference types
                // are intentionally not handled — their layout is platform- or
                // runtime-dependent, so we bail rather than risk a wrong suggestion.
                size = align = 0;
                return false;
        }
    }
}
