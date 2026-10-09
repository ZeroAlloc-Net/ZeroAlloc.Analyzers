using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

/// <summary>
/// Reports a net8.0+ library that never decided on IsAotCompatible. Until it does, the SDK's
/// trim and AOT analyzers are off, so AOT hazards surface only when an app publishes with it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MarkLibraryAotCompatibleAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.MarkLibraryAotCompatible,
        "Mark libraries as AOT-compatible",
        "Library does not set IsAotCompatible, so the SDK's trim and AOT analyzers are off; set it for net8.0 and later target frameworks",
        DiagnosticCategories.Aot,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationAction(static compilationContext =>
        {
            // Apps, test projects and older frameworks report nothing.
            if (AppliesTo(compilationContext.Compilation, compilationContext.Options))
                compilationContext.ReportDiagnostic(Diagnostic.Create(Rule, Location.None));
        });
    }

    private static bool AppliesTo(Compilation compilation, AnalyzerOptions options) =>
        compilation.Options.OutputKind == OutputKind.DynamicallyLinkedLibrary
        && !AotHelper.IsBuildPropertyTrue(options, "IsTestProject")
        && TfmHelper.TryGetTfm(options, out var tfm)
        && TfmHelper.IsNet8OrLater(tfm)
        && !AotHelper.IsSdkAotAnalyzerEnabled(options)
        // An explicit IsAotCompatible=false is a decision too; only an unset property is reported.
        && !AotHelper.IsBuildPropertySet(options, "IsAotCompatible");
}
