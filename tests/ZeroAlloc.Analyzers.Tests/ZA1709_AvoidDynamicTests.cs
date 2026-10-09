using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1709_AvoidDynamicTests
{
    private static string WithDynamic(string body) => $$"""
        using System;

        class C
        {
            object M(dynamic d, object c)
            {
                {{body}}
            }
        }
        """;

    private static DiagnosticResult Expected() =>
        CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.Diagnostic(DiagnosticIds.AvoidDynamic).WithLocation(0);

    [Theory]
    [InlineData("return {|#0:d.Name|};")]
    [InlineData("return {|#0:d.Compute(1, 2)|};")]
    [InlineData("return {|#0:d[0]|};")]
    [InlineData("return {|#0:d.A.B(c)|};")]
    [InlineData("return {|#0:d + 1|};")]
    [InlineData("int n = {|#0:d.Count|}; return n;")]
    [InlineData("{|#0:d.Count++|}; return null;")]
    [InlineData("{|#0:Console.WriteLine(d)|}; return null;")]
    [InlineData("return {|#0:new Uri(d)|};")]
    public async Task DynamicDispatch_ReportsOncePerExpression(string body)
    {
        // Each case is one expression; nested dynamic operations report only at the outermost one.
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerAsync(WithDynamic(body), "net8.0", Expected());
    }

    [Theory]
    [InlineData("dynamic x = new object(); return x;")]
    [InlineData("dynamic x = c; x = 5; return x;")]
    [InlineData("return d;")]
    public async Task DynamicWithoutDispatch_NoDiagnostic(string body)
    {
        // Storing into, assigning or returning a dynamic value does not call the runtime binder.
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyNoDiagnosticAsync(WithDynamic(body), "net8.0");
    }

    [Theory]
    [InlineData("PublishAot")]
    [InlineData("IsAotCompatible")]
    public async Task SdkAotAnalyzerOn_NoDiagnostic(string property)
    {
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerWithPropertiesAsync(
            WithDynamic("return d.Name;"),
            ReferenceAssemblies.Net.Net80,
            new Dictionary<string, string> { ["TargetFramework"] = "net8.0", [property] = "true" },
            []);
    }
}
