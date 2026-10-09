using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0110_PreferParamsSpanTests
{
    private const string ExposedSuffix = "; it is visible outside the assembly, so add an overload instead of changing it";

    private static DiagnosticResult Expected(string method, string suffix = "", string elementType = "int", string parameter = "values") =>
        CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .Diagnostic(DiagnosticIds.PreferParamsSpan)
            .WithLocation(0)
            .WithArguments(parameter, method, elementType, suffix);

    private static CSharpAnalyzerTest<PreferParamsSpanAnalyzer, DefaultVerifier> NewAnalyzerTest(string source)
    {
        var test = new CSharpAnalyzerTest<PreferParamsSpanAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AnalyzerConfigFiles.Add(
            ("/.globalconfig", """
                is_global = true
                build_property.TargetFramework = net8.0
                """));
        return test;
    }

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

    [Fact]
    public async Task JaggedArray_FixKeepsInnerArrayType()
    {
        var source = """
            using System;

            class C
            {
                static int Sum(params int[][] {|#0:rows|}) => rows.Length;
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                static int Sum(params ReadOnlySpan<int[]> rows) => rows.Length;
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Sum", elementType: "int[]", parameter: "rows"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task NamespaceNotImported_FixKeepsTypeResolvable()
    {
        var source = """
            using System;

            class C
            {
                static int Count(params System.Text.StringBuilder[] {|#0:items|}) => items.Length;
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                static int Count(params ReadOnlySpan<System.Text.StringBuilder> items) => items.Length;
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Count", elementType: "StringBuilder", parameter: "items"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task MissingUsingSystem_FixAddsIt()
    {
        var source = """
            class C
            {
                static int Sum(params int[] {|#0:values|}) => values.Length;
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                static int Sum(params ReadOnlySpan<int> values) => values.Length;
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Sum"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task GenericMethod_ReportsAndFixes()
    {
        var source = """
            using System;

            class C
            {
                static int Count<T>(params T[] {|#0:items|}) => items.Length;
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                static int Count<T>(params ReadOnlySpan<T> items) => items.Length;
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Count", elementType: "T", parameter: "items"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task ExposedMethod_OffersNoFix()
    {
        var source = """
            public class C
            {
                public static int Sum(params int[] {|#0:values|}) => values.Length;
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(source, source, Expected("Sum", ExposedSuffix), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task PrimaryConstructorParameter_NoDiagnostic()
    {
        var source = """
            class B { }

            class C(params int[] values) : B()
            {
                int Count => values.Length;
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task MethodGroupInGeneratedFile_NoDiagnostic()
    {
        var test = NewAnalyzerTest("""
            partial class C
            {
                static int Keep(params int[] values) => values.Length;
            }
            """);
        test.TestState.Sources.Add(("Generated.g.cs", """
            // <auto-generated/>
            using System;

            partial class C
            {
                static Func<int[], int> Group = Keep;
            }
            """));

        await test.RunAsync();
    }

    [Fact]
    public async Task MethodInGeneratedFile_NoDiagnostic()
    {
        var test = NewAnalyzerTest("class D { }");
        test.TestState.Sources.Add(("Generated.g.cs", """
            // <auto-generated/>
            class C
            {
                static int Keep(params int[] values) => values.Length;
            }
            """));

        await test.RunAsync();
    }

    [Fact]
    public async Task InterfaceImplementedByInheritedMethod_NoDiagnostic()
    {
        var source = """
            interface ISummer
            {
                int Sum(params int[] values);
            }

            class Base
            {
                public int Sum(params int[] values) => values.Length;
            }

            class D : Base, ISummer
            {
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Theory]
    [InlineData("object", "static int Bar() => Foo(new string[0]);")]
    [InlineData("object", "static int Bar() => Foo(null);")]
    [InlineData("int", "static System.Linq.Expressions.Expression<Func<int>> Bar() => () => Foo(1, 2);")]
    public async Task CallerThatBreaksOrChangesMeaning_NoDiagnostic(string element, string caller)
    {
        var source = $$"""
            using System;

            class C
            {
                static int Foo(params {{element}}[] values) => values.Length;

                {{caller}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task CallerPassingExactArray_StillReports()
    {
        var source = """
            class C
            {
                static int Foo(params int[] {|#0:values|}) => values.Length;

                static int Bar(int[] existing) => Foo(existing);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("Foo"));
    }

    [Fact]
    public async Task NoInlineArraySupport_NoDiagnostic()
    {
        var source = """
            class C
            {
                static int Sum(params int[] values) => values.Length;
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net6.0", ReferenceAssemblies.Net.Net60);
    }

    [Theory]
    [InlineData("internal static int Sum(params int[] {|#0:values|}) => values.Length;")]
    [InlineData("public static int Sum(params int[] {|#0:values|}) => values.Length;")]
    public async Task InternalMethodWithFriendAssembly_ReportsOverloadAdvice(string member)
    {
        var source = $$"""
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Other")]

            internal class C
            {
                {{member}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("Sum", ExposedSuffix));
    }
}
