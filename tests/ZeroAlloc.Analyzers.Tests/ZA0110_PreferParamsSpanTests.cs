using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0110_PreferParamsSpanTests
{
    private const string ExposedSuffix = "; it is visible outside the assembly, so add an overload instead of changing it";

    private static DiagnosticResult Expected(string method, string suffix = "") =>
        CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .Diagnostic(DiagnosticIds.PreferParamsSpan)
            .WithLocation(0)
            .WithArguments("values", method, "int", suffix);

    [Fact]
    public async Task PrivateMethodReadingTheArray_ReportsAndFixes()
    {
        var source = """
            using System;

            class C
            {
                static int Sum(params int[] {|#0:values|})
                {
                    var total = values.Length + values[0];
                    foreach (var value in values)
                        total += value;
                    return total;
                }
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                static int Sum(params ReadOnlySpan<int> values)
                {
                    var total = values.Length + values[0];
                    foreach (var value in values)
                        total += value;
                    return total;
                }
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Sum"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task PassedOnAsSpan_Reports()
    {
        var source = """
            using System;

            class C
            {
                static int Inner(ReadOnlySpan<int> span) => span.Length;

                static int Outer(params int[] {|#0:values|}) => Inner(values);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("Outer"));
    }

    [Fact]
    public async Task PublicMethod_ReportsOverloadAdvice()
    {
        var source = """
            public class C
            {
                public static int Sum(params int[] {|#0:values|}) => values.Length;
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("Sum", ExposedSuffix));
    }

    [Fact]
    public async Task CSharp12_NoDiagnostic()
    {
        // params collections arrived in C# 13.
        var source = """
            class C
            {
                static int Sum(params int[] values) => values.Length;
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, LanguageVersion.CSharp12);
    }

    [Theory]
    [InlineData("static int[] Keep(params int[] values) => values;")]
    [InlineData("static int[] _kept = new int[0]; static void Keep(params int[] values) => _kept = values;")]
    [InlineData("static int Inner(int[] array) => array.Length; static int Keep(params int[] values) => Inner(values);")]
    [InlineData("static void Keep(params int[] values) => values[0] = 1;")]
    [InlineData("static Func<int> Keep(params int[] values) => () => values.Length;")]
    [InlineData("static int Keep(params int[] values) => values.Length; static Func<int[], int> Group = Keep;")]
    [InlineData("static IEnumerable<int> Keep(params int[] values) { foreach (var v in values) yield return v; }")]
    [InlineData("static async Task<int> Keep(params int[] values) { await Task.Yield(); return values.Length; }")]
    [InlineData("public virtual int Keep(params int[] values) => values.Length;")]
    public async Task ArrayIsNeededOrSignatureIsFixed_NoDiagnostic(string member)
    {
        // Escapes, writes, captures, method groups, iterators, async, and virtual methods.
        var source = $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;

            class C
            {
                {{member}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task OverrideAndInterfaceImplementation_NoDiagnostic()
    {
        var source = """
            interface ISummer
            {
                int Sum(params int[] values);
            }

            abstract class Base
            {
                public abstract int Count(params int[] values);
            }

            class C : Base, ISummer
            {
                public int Sum(params int[] values) => values.Length;

                public override int Count(params int[] values) => values.Length;
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
