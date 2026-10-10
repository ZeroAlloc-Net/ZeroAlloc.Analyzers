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

            static void Use(object a, object b) { }
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
    [InlineData("foreach (var x in {|#0:d|}) { } return null;")]
    [InlineData("if ({|#0:d|}) { } return null;")]
    [InlineData("return d?{|#0:.Name|};")]
    [InlineData("return {|#0:(string)d|};")]
    [InlineData("return {|#0:d == null|};")]
    [InlineData("{|#0:Use(d.A, d.B)|}; return null;")]
    public async Task DynamicDispatch_ReportsOncePerExpression(string body)
    {
        // Each case is one expression; nested dynamic operations report only at the outermost one.
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerAsync(WithDynamic(body), "net8.0", Expected());
    }

    [Theory]
    [InlineData("dynamic x = new object(); return x;")]
    [InlineData("dynamic x = c; x = 5; return x;")]
    [InlineData("return d;")]
    [InlineData("return d is string;")]
    [InlineData("return d as string;")]
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

    [Fact]
    public async Task AwaitDynamic_Reports()
    {
        // Awaiting a dynamic value binds GetAwaiter, IsCompleted and GetResult at run time.
        const string source = """
            using System.Threading.Tasks;

            class C
            {
                async Task<object> M(dynamic d)
                {
                    return {|#0:await d|};
                }
            }
            """;
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerAsync(source, "net8.0", Expected());
    }

    [Fact]
    public async Task Message_SpellsNativeAot()
    {
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerAsync(
            WithDynamic("return {|#0:d.Name|};"),
            "net8.0",
            Expected().WithMessage("'dynamic' dispatch uses the runtime binder, which is not supported under Native AOT"));
    }

    [Theory]
    [InlineData("using ({|#0:d|}) { } return null;")]
    [InlineData("using ({|#0:d.Open()|}) { } return null;")]
    [InlineData("using ({|#0:d.A + c|}) { } return null;")]
    [InlineData("using (dynamic {|#0:x = c|}) { } return null;")]
    [InlineData("using (var {|#0:x = d|}) { } return null;")]
    [InlineData("using (dynamic x = {|#0:d.Open()|}) { } return null;")]
    [InlineData("using dynamic {|#0:x = c|}; return null;")]
    [InlineData("using var {|#0:x = d|}; return null;")]
    public async Task UsingDynamicResource_ReportsOnce(string body)
    {
        // Disposing a dynamic resource binds its conversion to IDisposable at run time.
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerAsync(WithDynamic(body), "net8.0", Expected());
    }

    [Fact]
    public async Task UsingDynamicResource_BodyStillReports()
    {
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyAnalyzerAsync(
            WithDynamic("using ({|#0:d|}) { {|#1:d.Close()|}; } return null;"),
            "net8.0",
            Expected(),
            CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.Diagnostic(DiagnosticIds.AvoidDynamic).WithLocation(1));
    }

    [Theory]
    [InlineData("using ((IDisposable)c) { } return null;")]
    [InlineData("using (d as IDisposable) { } return null;")]
    [InlineData("using (IDisposable x = (IDisposable)c, y = null) { } return null;")]
    public async Task UsingStaticResource_NoDiagnostic(string body)
    {
        await CSharpAnalyzerVerifier<AvoidDynamicAnalyzer>.VerifyNoDiagnosticAsync(WithDynamic(body), "net8.0");
    }
}
