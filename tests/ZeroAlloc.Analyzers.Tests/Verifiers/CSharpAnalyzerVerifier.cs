using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Testing;

namespace ZeroAlloc.Analyzers.Tests.Verifiers;

public static class CSharpAnalyzerVerifier<TAnalyzer>
    where TAnalyzer : DiagnosticAnalyzer, new()
{
    public static DiagnosticResult Diagnostic(string diagnosticId)
        => CSharpAnalyzerVerifier<TAnalyzer, DefaultVerifier>.Diagnostic(diagnosticId);

    public static async Task VerifyAnalyzerAsync(
        string source,
        string targetFramework = "net8.0",
        params DiagnosticResult[] expected)
    {
        await RunAsync(source, targetFramework, ReferenceAssemblies.Net.Net80, languageVersion: null, expected);
    }

    public static async Task VerifyAnalyzerAsync(
        string source,
        string targetFramework,
        ReferenceAssemblies referenceAssemblies,
        params DiagnosticResult[] expected)
    {
        await RunAsync(source, targetFramework, referenceAssemblies, languageVersion: null, expected);
    }

    /// <summary>Analyzes with .NET 8 reference assemblies at the given C# language version.</summary>
    public static async Task VerifyAnalyzerAsync(
        string source,
        LanguageVersion languageVersion,
        params DiagnosticResult[] expected)
    {
        await RunAsync(source, "net8.0", ReferenceAssemblies.Net.Net80, languageVersion, expected);
    }

    /// <summary>Analyzes with the given reference assemblies at the given C# language version.</summary>
    public static async Task VerifyAnalyzerAsync(
        string source,
        string targetFramework,
        ReferenceAssemblies referenceAssemblies,
        LanguageVersion languageVersion,
        params DiagnosticResult[] expected)
    {
        await RunAsync(source, targetFramework, referenceAssemblies, languageVersion, expected);
    }

    public static async Task VerifyNoDiagnosticAsync(
        string source,
        string targetFramework = "net8.0")
    {
        await VerifyAnalyzerAsync(source, targetFramework);
    }

    public static async Task VerifyNoDiagnosticAsync(
        string source,
        string targetFramework,
        ReferenceAssemblies referenceAssemblies)
    {
        await VerifyAnalyzerAsync(source, targetFramework, referenceAssemblies);
    }

    /// <summary>
    /// Analyzes with the given MSBuild properties visible to the analyzer, as the package's
    /// buildTransitive props make them, and with the given output kind.
    /// </summary>
    public static async Task VerifyAnalyzerWithPropertiesAsync(
        string source,
        ReferenceAssemblies referenceAssemblies,
        IReadOnlyDictionary<string, string> buildProperties,
        DiagnosticResult[] expected,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = referenceAssemblies,
        };

        test.TestState.OutputKind = outputKind;

        var properties = string.Join("\n", buildProperties.Select(p => $"build_property.{p.Key} = {p.Value}"));
        test.TestState.AnalyzerConfigFiles.Add(("/.globalconfig", $"is_global = true\n{properties}\n"));

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    private static async Task RunAsync(
        string source,
        string targetFramework,
        ReferenceAssemblies referenceAssemblies,
        LanguageVersion? languageVersion,
        DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<TAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = referenceAssemblies,
        };

        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", $"""
                is_global = true
                build_property.TargetFramework = {targetFramework}
                """));

        if (languageVersion is { } version)
        {
            test.SolutionTransforms.Add((solution, projectId) =>
            {
                var parseOptions = (CSharpParseOptions)solution.GetProject(projectId)!.ParseOptions!;
                return solution.WithProjectParseOptions(projectId, parseOptions.WithLanguageVersion(version));
            });
        }

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
