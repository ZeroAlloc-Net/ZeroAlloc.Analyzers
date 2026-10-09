using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace ZeroAlloc.Analyzers.Tests.Verifiers;

public static class CSharpCodeFixVerifier<TAnalyzer, TCodeFix>
    where TAnalyzer : DiagnosticAnalyzer, new()
    where TCodeFix : CodeFixProvider, new()
{
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyCodeFixAsync(
        string source,
        string fixedSource,
        string diagnosticId,
        string targetFramework = "net8.0")
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", $"""
                is_global = true
                build_property.TargetFramework = {targetFramework}
                """));

        test.ExpectedDiagnostics.Add(Diagnostic(diagnosticId).WithLocation(0));
        await test.RunAsync();
    }

    public static async Task VerifyCodeFixAsync(
        string source,
        string fixedSource,
        DiagnosticResult expected,
        string targetFramework = "net8.0",
        bool compilationEndDiagnostic = false)
    {
        var test = new CSharpCodeFixTest<TAnalyzer, TCodeFix, DefaultVerifier>
        {
            TestCode = source,
            FixedCode = fixedSource,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        // A compilation-end diagnostic is never produced by the per-document analysis the harness uses to
        // check that a fix targets a local diagnostic.
        if (compilationEndDiagnostic)
            test.CodeFixTestBehaviors |= CodeFixTestBehaviors.SkipLocalDiagnosticCheck;

        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", $"""
                is_global = true
                build_property.TargetFramework = {targetFramework}
                """));

        test.ExpectedDiagnostics.Add(expected);
        await test.RunAsync();
    }
}
