using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class PreferParamsSpanAnalyzer : DiagnosticAnalyzer
{
    public const string ExposedProperty = "Exposed";

    private const string ExposedSuffix = "; it is visible outside the assembly, so add an overload instead of changing it";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.PreferParamsSpan,
        "Declare params as ReadOnlySpan<T>",
        "params parameter '{0}' of '{1}' can be 'params ReadOnlySpan<{2}>' so callers do not allocate{3}",
        DiagnosticCategories.Collections,
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
            if (start.Compilation is not CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp13 })
                return;

            var readOnlySpan = start.Compilation.GetTypeByMetadataName("System.ReadOnlySpan`1");
            if (readOnlySpan is null)
                return;

            var candidates = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);
            var methodGroups = new ConcurrentDictionary<IMethodSymbol, bool>(SymbolEqualityComparer.Default);

            // A method group converted to a delegate fixes the parameter type.
            start.RegisterOperationAction(context =>
            {
                var reference = (IMethodReferenceOperation)context.Operation;
                methodGroups[reference.Method.OriginalDefinition] = true;
            }, OperationKind.MethodReference);

            start.RegisterOperationBlockAction(context =>
            {
                if (context.OwningSymbol is IMethodSymbol method
                    && IsCandidate(method)
                    && UsesAreSpanCompatible(context.OperationBlocks, method.Parameters[method.Parameters.Length - 1], readOnlySpan))
                {
                    candidates[method] = true;
                }
            });

            start.RegisterCompilationEndAction(context =>
            {
                foreach (var method in candidates.Keys)
                {
                    if (methodGroups.ContainsKey(method))
                        continue;

                    var parameter = method.Parameters[method.Parameters.Length - 1];
                    var exposed = IsVisibleOutsideAssembly(method);
                    var properties = ImmutableDictionary<string, string?>.Empty
                        .Add(ExposedProperty, exposed ? "true" : "false");

                    context.ReportDiagnostic(Diagnostic.Create(
                        Rule,
                        parameter.Locations[0],
                        properties,
                        parameter.Name,
                        method.Name,
                        ((IArrayTypeSymbol)parameter.Type).ElementType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                        exposed ? ExposedSuffix : string.Empty));
                }
            });
        });
    }

    private static bool IsCandidate(IMethodSymbol method)
    {
        if (method.MethodKind is not (MethodKind.Ordinary or MethodKind.Constructor)
            || method.Parameters.IsEmpty
            || method.Parameters[method.Parameters.Length - 1] is not { IsParams: true, Type: IArrayTypeSymbol { Rank: 1 } })
        {
            return false;
        }

        // The signature is fixed by another declaration, or a span parameter is not allowed.
        if (method.IsOverride || method.IsVirtual || method.IsAbstract || method.IsExtern
            || method.IsAsync || method.IsIterator
            || method.PartialDefinitionPart is not null || method.PartialImplementationPart is not null
            || !method.ExplicitInterfaceImplementations.IsEmpty)
        {
            return false;
        }

        foreach (var iface in method.ContainingType.AllInterfaces)
        {
            foreach (var member in iface.GetMembers().OfType<IMethodSymbol>())
            {
                if (SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method))
                    return false;
            }
        }

        return true;
    }

    private static bool UsesAreSpanCompatible(ImmutableArray<IOperation> blocks, IParameterSymbol parameter, INamedTypeSymbol readOnlySpan)
    {
        foreach (var block in blocks)
        {
            foreach (var reference in block.Descendants().OfType<IParameterReferenceOperation>())
            {
                if (!SymbolEqualityComparer.Default.Equals(reference.Parameter, parameter))
                    continue;

                // A span cannot be captured by a lambda or local function.
                for (var current = reference.Parent; current is not null; current = current.Parent)
                {
                    if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
                        return false;
                }

                if (!ReadOnlySpanUse.IsCompatible(reference, readOnlySpan))
                    return false;
            }
        }

        return true;
    }

    private static bool IsVisibleOutsideAssembly(ISymbol symbol)
    {
        for (var current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingSymbol)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
                return false;
        }

        return true;
    }
}
