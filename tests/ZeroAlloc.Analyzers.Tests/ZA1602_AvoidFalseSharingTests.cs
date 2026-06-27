using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1602_AvoidFalseSharingTests
{
    /// <summary>
    /// Helper that enables ZA1602 (disabled by default) via .globalconfig.
    /// </summary>
    private static async Task VerifyWithRuleEnabled(
        string source,
        params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<AvoidFalseSharingAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };

        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", """
                is_global = true
                build_property.TargetFramework = net8.0
                dotnet_diagnostic.ZA1602.severity = info
                """));

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }

    [Fact]
    public async Task InterlockedFieldWithNeighbour_Reports()
    {
        var source = """
            using System.Threading;

            class Counter
            {
                private long {|#0:_a|};
                private long _b;

                public void Inc() => Interlocked.Increment(ref _a);
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidFalseSharingAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidFalseSharing)
            .WithLocation(0)
            .WithArguments("_a", "Counter");

        await VerifyWithRuleEnabled(source, expected);
    }

    [Fact]
    public async Task DisabledByDefault_NoDiagnostic()
    {
        var source = """
            using System.Threading;

            class Counter
            {
                private long _a;
                private long _b;

                public void Inc() => Interlocked.Increment(ref _a);
            }
            """;

        await CSharpAnalyzerVerifier<AvoidFalseSharingAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task SingleField_NoDiagnostic()
    {
        var source = """
            using System.Threading;

            class Counter
            {
                private long _a;

                public void Inc() => Interlocked.Increment(ref _a);
            }
            """;

        await VerifyWithRuleEnabled(source);
    }

    [Fact]
    public async Task ExplicitLayout_NoDiagnostic()
    {
        var source = """
            using System.Threading;
            using System.Runtime.InteropServices;

            [StructLayout(LayoutKind.Explicit)]
            class Counter
            {
                [FieldOffset(0)] private long _a;
                [FieldOffset(64)] private long _b;

                public void Inc() => Interlocked.Increment(ref _a);
            }
            """;

        await VerifyWithRuleEnabled(source);
    }

    [Fact]
    public async Task NonInterlockedField_NoDiagnostic()
    {
        var source = """
            class Counter
            {
                private long _a;
                private long _b;

                public void Inc() => _a++;
            }
            """;

        await VerifyWithRuleEnabled(source);
    }
}
