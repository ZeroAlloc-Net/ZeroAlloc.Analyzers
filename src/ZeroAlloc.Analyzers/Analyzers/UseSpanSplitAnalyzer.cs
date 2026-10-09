using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ZeroAlloc.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UseSpanSplitAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UseSpanSplit,
        "Use the span-based Split",
        "Enumerate '{0}.AsSpan().Split(...)' to split without allocating the parts",
        DiagnosticCategories.Strings,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(static start =>
        {
            // .NET 9 adds MemoryExtensions.Split<T> returning this enumerator.
            if (start.Compilation.GetTypeByMetadataName("System.MemoryExtensions+SpanSplitEnumerator`1") is null)
                return;

            // Before C# 13 a ref struct local is not allowed in an async method or an iterator.
            var refStructLocalsInAsync = start.Compilation is CSharpCompilation { LanguageVersion: >= LanguageVersion.CSharp13 };
            start.RegisterOperationAction(context => AnalyzeForEach(context, refStructLocalsInAsync), OperationKind.Loop);
        });
    }

    private static void AnalyzeForEach(OperationAnalysisContext context, bool refStructLocalsInAsync)
    {
        if (context.Operation is not IForEachLoopOperation loop)
            return;

        var collection = loop.Collection;
        while (collection is IConversionOperation { IsImplicit: true } conversion)
            collection = conversion.Operand;

        if (collection is not IInvocationOperation { TargetMethod: { Name: "Split" } method, Instance: { } receiver } split
            || method.ContainingType.SpecialType != SpecialType.System_String
            || method.Parameters.Length != 2
            || method.Parameters[1].Type.Name != "StringSplitOptions")
        {
            return;
        }

        var separator = split.Arguments.First(a => a.Parameter?.Ordinal == 0);
        var separatorIsSupported = method.Parameters[0].Type.SpecialType switch
        {
            SpecialType.System_Char => true,
            SpecialType.System_String => separator.Value.ConstantValue is { HasValue: true, Value: string { Length: > 0 } },
            _ => false,
        };

        var options = split.Arguments.First(a => a.Parameter?.Ordinal == 1);
        var noOptions = options.ArgumentKind == ArgumentKind.DefaultValue
            || options.Value.ConstantValue is { HasValue: true, Value: 0 };

        if (!separatorIsSupported || !noOptions)
            return;

        // The span enumerator is a ref struct, so it cannot be live across an await or a yield.
        if (AsyncOrIterator.ContainsAwaitOrYield(loop.Body)
            || !refStructLocalsInAsync && AsyncOrIterator.IsInAsyncOrIterator(loop, context.ContainingSymbol))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, split.Syntax.GetLocation(), receiver.Syntax.ToString()));
    }
}
