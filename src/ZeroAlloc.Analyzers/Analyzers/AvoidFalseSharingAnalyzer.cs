using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Heuristic, opt-in rule that flags fields updated with <c>Interlocked</c> operations
/// that share a containing type — and therefore likely a 64-byte CPU cache line — with
/// other instance fields. When two cores update independent fields that live on the same
/// cache line, each write invalidates the other core's copy of the whole line ("false
/// sharing"), stalling both threads invisibly.
///
/// This pattern cannot be proven statically (it depends on runtime threading), so the
/// rule is <b>disabled by default</b>. Enable it via .editorconfig:
/// <c>dotnet_diagnostic.ZA1602.severity = info</c>.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AvoidFalseSharingAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.AvoidFalseSharing,
        "Isolate Interlocked-updated fields to avoid false sharing",
        "Field '{0}' is updated with Interlocked but shares a cache line with other fields in '{1}' — isolate hot fields on their own 64-byte cache line (e.g. [StructLayout(LayoutKind.Explicit)] with FieldOffset, or padding) to avoid false sharing",
        DiagnosticCategories.DataLayout,
        DiagnosticSeverity.Info,
        isEnabledByDefault: false,
        customTags: [WellKnownDiagnosticTags.CompilationEnd]);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var hotFields = new ConcurrentDictionary<IFieldSymbol, byte>(SymbolEqualityComparer.Default);

            compilationContext.RegisterOperationAction(operationContext =>
            {
                var invocation = (IInvocationOperation)operationContext.Operation;

                if (invocation.TargetMethod.ContainingType?.ToDisplayString() != "System.Threading.Interlocked")
                    return;

                // Every Interlocked method takes the storage location as its first (ref) argument.
                var firstArg = invocation.Arguments.FirstOrDefault();
                if (firstArg?.Value is IFieldReferenceOperation fieldRef
                    && fieldRef.Field is { IsStatic: false, IsConst: false } targetField
                    && !targetField.DeclaringSyntaxReferences.IsDefaultOrEmpty)
                {
                    hotFields.TryAdd(targetField, 0);
                }
            }, OperationKind.Invocation);

            compilationContext.RegisterCompilationEndAction(endContext =>
            {
                foreach (var hotField in hotFields.Keys)
                {
                    var containingType = hotField.ContainingType;

                    // Explicit layout means the author already controls field offsets.
                    if (HasExplicitLayout(containingType))
                        continue;

                    // Needs at least one neighbour to falsely share a cache line with.
                    int instanceFieldCount = containingType.GetMembers()
                        .OfType<IFieldSymbol>()
                        .Count(f => !f.IsStatic && !f.IsConst && !f.IsImplicitlyDeclared);

                    if (instanceFieldCount < 2)
                        continue;

                    var location = hotField.Locations.FirstOrDefault();
                    if (location == null || !location.IsInSource)
                        continue;

                    endContext.ReportDiagnostic(Diagnostic.Create(
                        Rule, location, hotField.Name, containingType.Name));
                }
            });
        });
    }

    private static bool HasExplicitLayout(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString()
                    != "System.Runtime.InteropServices.StructLayoutAttribute")
            {
                continue;
            }

            var arg = attribute.ConstructorArguments.FirstOrDefault();
            // LayoutKind.Explicit == 2
            if (arg.Value is int kind && kind == 2)
                return true;
        }

        return false;
    }
}
