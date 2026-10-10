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
    public async Task GlobalUsingSystemInOtherFile_FixAddsNoUsing()
    {
        var source = """
            class C
            {
                static int Sum(params int[] {|#0:values|}) => values.Length;
            }
            """;

        var fixedSource = """
            class C
            {
                static int Sum(params ReadOnlySpan<int> values) => values.Length;
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixWithOtherDocumentsAsync(
                source, fixedSource, Expected("Sum"), ["global using System;"], compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task UsingSystemInsideNamespace_FixAddsNoUsing()
    {
        var source = """
            namespace N
            {
                using System;

                class C
                {
                    static int Sum(params int[] {|#0:values|}) => values.Length;
                }
            }
            """;

        var fixedSource = """
            namespace N
            {
                using System;

                class C
                {
                    static int Sum(params ReadOnlySpan<int> values) => values.Length;
                }
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Sum"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task OnlyAnAliasOfSystem_FixKeepsTypeResolvable()
    {
        // An alias is not an import of the namespace, so ReadOnlySpan alone would not resolve.
        var source = """
            using S = System;

            class C
            {
                static int Sum(params int[] {|#0:values|}) => values.Length;
            }
            """;

        var fixedSource = """
            using System;
            using S = System;

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
    public async Task GenericMethodWithoutSystemImport_FixAddsIt()
    {
        // The element type is the method's own type parameter, whose identity includes the
        // signature the fix changes.
        var source = """
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

    [Theory]
    [InlineData("static ReadOnlySpan<int> Pass(params int[] values) => values;")]
    [InlineData("static ReadOnlySpan<int> Pass(params int[] values) { ReadOnlySpan<int> span = values; return span; }")]
    [InlineData("static int Pass(params int[] values) { ReadOnlySpan<int> span = values; return span.Length; }")]
    [InlineData("static ref readonly int Pass(params int[] values) => ref values[0];")]
    [InlineData("static void Pass(out ReadOnlySpan<int> span, params int[] values) => span = values;")]
    [InlineData("static void Pass(ref ReadOnlySpan<int> span, params int[] values) { }")]
    [InlineData("static void Copy(ref ReadOnlySpan<int> d, ReadOnlySpan<int> s) => d = s; static int Pass(params int[] values) { ReadOnlySpan<int> local = default; Copy(ref local, values); return local.Length; }")]
    [InlineData("ref struct R { ReadOnlySpan<int> _s; public void Set(params int[] values) => _s = values; }")]
    [InlineData("ref struct R { ReadOnlySpan<int> _s; public R(params int[] values) { _s = values; } }")]
    public async Task SpanWouldEscapeItsScope_NoDiagnostic(string member)
    {
        // A params span is implicitly scoped, so the fixed code could not let it escape.
        var source = $$"""
            using System;

            class C
            {
                {{member}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Theory]
    [InlineData("Same(values).Length")]
    [InlineData("Same(values)[0]")]
    [InlineData("Same(values).Slice(1).Length")]
    [InlineData("Same(Same(values)).Length")]
    [InlineData("Same(values).ToArray().Length")]
    [InlineData("Count(values)")]
    [InlineData("Count(Same(values))")]
    [InlineData("new Reader(values).Length")]
    public async Task SpanStaysInScopeThroughCalls_ReportsAndFixes(string body)
    {
        // A span returned by a call, or passed by 'in', cannot outlive the method unless it is stored or returned.
        const string helpers = """
                static ReadOnlySpan<int> Same(ReadOnlySpan<int> s) => s;
                static int Count(in ReadOnlySpan<int> s) => s.Length;
                ref struct Reader { private ReadOnlySpan<int> _s; public Reader(ReadOnlySpan<int> s) => _s = s; public int Length => _s.Length; }
            """;

        var source = $$"""
            using System;

            class C
            {
            {{helpers}}
                static int Pass(params int[] {|#0:values|}) => {{body}};
            }
            """;

        var fixedSource = $$"""
            using System;

            class C
            {
            {{helpers}}
                static int Pass(params ReadOnlySpan<int> values) => {{body}};
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                fixedSource,
                Expected("Pass"),
                compilationEndDiagnostic: true,
                languageVersion: LanguageVersion.CSharp13);
    }

    [Fact]
    public async Task SpanThroughForeachOverCall_Reports()
    {
        var source = """
            using System;

            class C
            {
                static ReadOnlySpan<int> Same(ReadOnlySpan<int> s) => s;

                static int Pass(params int[] {|#0:values|})
                {
                    var total = 0;
                    foreach (var value in Same(values))
                        total += value;
                    return total;
                }
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyAnalyzerAsync(source, "net8.0", Expected("Pass"));
    }

    [Theory]
    // Stored in a local, the span would need scoped-local flow analysis.
    [InlineData("{ ReadOnlySpan<int> local = Same(values); return local.Length; }")]
    // Another argument could receive the span through ref or out.
    [InlineData("{ ReadOnlySpan<int> local = default; Copy(ref local, Same(values)); return local.Length; }")]
    [InlineData("=> Split(Same(values), out _);")]
    // A by-ref return could point into the span.
    [InlineData("=> Ref(Same(values));")]
    [InlineData("=> Ref(values);")]
    public async Task SpanThroughCallsMayEscape_NoDiagnostic(string body)
    {
        var source = $$"""
            using System;

            class C
            {
                static ReadOnlySpan<int> Same(ReadOnlySpan<int> s) => s;
                static void Copy(ref ReadOnlySpan<int> d, ReadOnlySpan<int> s) => d = s;
                static int Split(ReadOnlySpan<int> s, out ReadOnlySpan<int> rest) { rest = s; return 0; }
                static ref readonly int Ref(in ReadOnlySpan<int> s) => ref s[0];

                static int Pass(params int[] values) {{body}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Theory]
    [InlineData("static void Log(string message) { }", "Log(\"a\"); Log(\"a\", 1, 2);")]
    [InlineData("static void Log(string message, object first, object second) { }", "Log(\"a\", 1, 2); Log(\"a\", 1, 2, 3);")]
    [InlineData("static void Log(string message, object item) { }", "Log(\"a\", 1); Log(\"a\", 1, 2);")]
    [InlineData("static void Log(string message, IEnumerable<object> items) { }", "Log(\"a\", new List<object>()); Log(\"a\", 1, 2);")]
    [InlineData("static void Log<T>(string message, T item) { }", "Log(\"a\", 1); Log(\"a\", 1, 2);")]
    public async Task OverloadThatCannotConflict_ReportsAndFixes(string overload, string calls)
    {
        // Calls that pass separate arguments compare element conversions, which the fix does not change.
        var source = $$"""
            using System;
            using System.Collections.Generic;

            class C
            {
                {{overload}}
                static int Log(string message, params object[] {|#0:args|}) => args.Length;

                void M() { {{calls}} }
            }
            """;

        var fixedSource = $$"""
            using System;
            using System.Collections.Generic;

            class C
            {
                {{overload}}
                static int Log(string message, params ReadOnlySpan<object> args) => args.Length;

                void M() { {{calls}} }
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                fixedSource,
                Expected("Log", elementType: "object", parameter: "args"),
                compilationEndDiagnostic: true,
                languageVersion: LanguageVersion.CSharp13);
    }

    [Fact]
    public async Task ConstructorWithOverloadThatCannotConflict_Reports()
    {
        var source = """
            class C
            {
                C(params int[] {|#0:values|}) { _ = values.Length; }
                C(string name) { }
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyAnalyzerAsync(source, "net8.0", Expected("C"));
    }

    [Fact]
    public async Task ExposedMethodWithOverloadAcceptingTheArray_ReportsOverloadAdvice()
    {
        // The advice keeps the array overload, so a caller passing an array still binds to it.
        var source = """
            public class C
            {
                public static void Log(string message, object item) { }
                public static int Log(string message, params object[] {|#0:args|}) => args.Length;

                void M(object[] items) => Log("a", items);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("Log", ExposedSuffix, "object", "args"));
    }

    [Theory]
    // The fixed signature would duplicate an existing one.
    [InlineData("static int Sum(params int[] values) => values.Length; static int Sum(ReadOnlySpan<int> values) => 0;")]
    [InlineData("static int Sum(params int[] values) => values.Length; static int Sum(ReadOnlySpan<long> values) => 0;")]
    // Tie-breaks between two params methods depend on the collection type.
    [InlineData("static int Sum(params int[] values) => values.Length; static int Sum(params IEnumerable<int> values) => 0;")]
    [InlineData("static int Sum(params int[] values) => values.Length; static int Sum(string name, params int[] values) => 0;")]
    // A caller passes an array, which another overload could then win or make ambiguous.
    [InlineData("static int Sum(params int[] values) => values.Length; static int Sum(IEnumerable<int> values) => 0; int M(int[] a) => Sum(a);")]
    [InlineData("static int Sum(params int[] values) => values.Length; static int Sum(object values) => 0; int M() => Sum(new[] { 1 });")]
    [InlineData("static int Sum(params int[] values) => values.Length; static int Sum<T>(T values) => 0; int M(int[] a) => Sum(a);")]
    [InlineData("static int Sum(params int[] values) => values.Length; static int Sum(object other = null, object values = null) => 0; int M(int[] a) => Sum(values: a);")]
    public async Task OverloadThatCanConflict_NoDiagnostic(string members)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;

            class C
            {
                {{members}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ArrayCallWithOverloadAcceptingIt_NoDiagnostic()
    {
        // In C# 13, Count("a", items) would be ambiguous between object and params ReadOnlySpan<object>.
        var source = """
            class C
            {
                static void Count(string message, object item) { }
                static int Count(string message, params object[] items) => items.Length;

                void M(object[] items) => Count("a", items);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task OverloadInBaseType_WithoutArrayCall_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class B
            {
                protected static int Sum(IEnumerable<int> values) => 0;
            }

            class C : B
            {
                static int Sum(params int[] {|#0:values|}) => values.Length;

                int M() => Sum(1, 2);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyAnalyzerAsync(source, "net8.0", Expected("Sum"));
    }

    [Fact]
    public async Task OverloadInBaseType_WithArrayCall_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class B
            {
                protected static int Sum(IEnumerable<int> values) => 0;
            }

            class C : B
            {
                static int Sum(params int[] values) => values.Length;

                int M(int[] a) => Sum(a);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ExtensionMethod_WithoutArrayCall_Reports()
    {
        var source = """
            class D { }

            static class E
            {
                public static int Sum(this D d, params int[] {|#0:values|}) => values.Length;
            }

            class C
            {
                int M(D d) => d.Sum(1, 2);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyAnalyzerAsync(source, "net8.0", Expected("Sum"));
    }

    [Fact]
    public async Task ExtensionMethod_WithArrayCall_NoDiagnostic()
    {
        // Extension overloads in other static classes cannot be enumerated, so an array call blocks.
        var source = """
            class D { }

            static class E
            {
                public static int Sum(this D d, params int[] values) => values.Length;
            }

            static class Other
            {
                public static int Sum(this D d, object values) => 0;
            }

            class C
            {
                int M(D d, int[] a) => d.Sum(a);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Theory]
    // After the change these arguments convert to the span, so the params method could take the call.
    [InlineData("static void W(object o) { } static int W(params char[] cs) => cs.Length; void M() => W(\"abc\");")]
    [InlineData("static void W(IEnumerable<char> o) { } static int W(params char[] cs) => cs.Length; void M() => W(\"abc\");")]
    [InlineData("static void Log(string m, Span<int> s) { } static int Log(string m, params int[] v) => v.Length; void M() => Log(\"a\", [1, 2]);")]
    [InlineData("static void Log(string m, ReadOnlySpan<int> s, int y = 0) { } static int Log(string m, params int[] v) => v.Length; void M() => Log(\"a\", [1, 2]);")]
    [InlineData("static void P(ReadOnlySpan<byte> s, int x = 0) { } static int P(params byte[] b) => b.Length; void M() => P(\"abc\"u8);")]
    [InlineData("static void P(Span<int> v, int x = 0) { } static int P(params int[] v) => v.Length; void M(Span<int> s) => P(v: s);")]
    [InlineData("static int Count<T>(params T[] v) => v.Length; static int Count(Span<int> s) => 0; int M() => Count([1, 2]);")]
    public async Task CallToOtherOverloadCouldRebind_NoDiagnostic(string members)
    {
        var source = $$"""
            using System;
            using System.Collections.Generic;

            class C
            {
                {{members}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Theory]
    [InlineData("static C Make() => new C([1, 2]);")]
    [InlineData("static C Make() { C c = new([3]); return c; }")]
    [InlineData("C() : this([4]) { }")]
    public async Task ConstructorCallToOtherOverloadCouldRebind_NoDiagnostic(string member)
    {
        var source = $$"""
            using System;

            class C
            {
                C(Span<int> s) { }
                C(params int[] values) { _ = values.Length; }
                {{member}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ExtensionCallToOtherOverloadCouldRebind_NoDiagnostic()
    {
        var source = """
            using System;

            class D { }

            static class E
            {
                public static int Sum(this D d, params int[] values) => values.Length;
            }

            static class Other
            {
                public static int Sum(this D d, Span<int> values) => 0;
            }

            class C
            {
                int M(D d) => d.Sum([1, 2]);
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task CallToOtherOverloadThatCannotRebind_ReportsAndFixes()
    {
        // An int does not convert to a span of char, so W(5) keeps binding to W(object).
        var source = """
            using System;

            class C
            {
                static void W(object o) { }
                static int W(params char[] {|#0:cs|}) => cs.Length;

                void M() { W(5); W('a', 'b'); }
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                static void W(object o) { }
                static int W(params ReadOnlySpan<char> cs) => cs.Length;

                void M() { W(5); W('a', 'b'); }
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                fixedSource,
                Expected("W", elementType: "char", parameter: "cs"),
                compilationEndDiagnostic: true,
                languageVersion: LanguageVersion.CSharp13);
    }

    [Theory]
    // The implicit 'ref this' of a ref struct instance method could store the span.
    [InlineData("{ var h = new Holder(); h.Set(values); return h.Length; }")]
    [InlineData("{ var h = new Holder(); h.Set(Same(values)); return h.Length; }")]
    public async Task SpanPassedToRefStructInstanceMethod_NoDiagnostic(string body)
    {
        var source = $$"""
            using System;

            ref struct Holder
            {
                private ReadOnlySpan<int> _s;
                public void Set(ReadOnlySpan<int> s) => _s = s;
                public readonly int Peek(ReadOnlySpan<int> s) => s.Length;
                public readonly int Length => _s.Length;
            }

            class C
            {
                static ReadOnlySpan<int> Same(ReadOnlySpan<int> s) => s;

                static int Pass(params int[] values) {{body}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task SpanPassedToReadonlyRefStructMethod_ReportsAndFixes()
    {
        var source = """
            using System;

            ref struct Holder
            {
                public readonly int Peek(ReadOnlySpan<int> s) => s.Length;
            }

            class C
            {
                static int Pass(params int[] {|#0:values|}) { var h = new Holder(); return h.Peek(values); }
            }
            """;

        var fixedSource = """
            using System;

            ref struct Holder
            {
                public readonly int Peek(ReadOnlySpan<int> s) => s.Length;
            }

            class C
            {
                static int Pass(params ReadOnlySpan<int> values) { var h = new Holder(); return h.Peek(values); }
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                fixedSource,
                Expected("Pass"),
                compilationEndDiagnostic: true,
                languageVersion: LanguageVersion.CSharp13);
    }

    [Fact]
    public async Task PrimaryConstructorBaseCallCouldRebind_NoDiagnostic()
    {
        var source = """
            class B
            {
                public B(object o) { }
                public B(params char[] cs) { _ = cs.Length; }
            }

            class D() : B("abc");
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Theory]
    // Calls the compiler makes for you, with no invocation syntax of their own.
    [InlineData("L M() => new L { \"abc\" };")]
    [InlineData("L M() { L l = [\"abc\"]; return l; }")]
    public async Task ImplicitAddCallCouldRebind_NoDiagnostic(string member)
    {
        var source = $$"""
            using System.Collections;

            class L : IEnumerable
            {
                public void Add(object o) { }
                public int Add(params char[] cs) => cs.Length;
                public IEnumerator GetEnumerator() => null;
            }

            class C
            {
                {{member}}
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task InterpolatedStringHandlerCallCouldRebind_NoDiagnostic()
    {
        var source = """
            using System.Runtime.CompilerServices;

            [InterpolatedStringHandler]
            struct H
            {
                public H(int literalLength, int formattedCount) { }
                public void AppendLiteral(string s) { }
                public void AppendFormatted(object o) { }
                public int AppendFormatted(params char[] cs) => cs.Length;
            }

            class C
            {
                static void Use(H h) { }
                void M() => Use($"x{"abc"}");
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task AttributeConstructor_NoDiagnostic()
    {
        // Attribute arguments cannot bind to a span parameter.
        var source = """
            using System;

            class TagsAttribute : Attribute
            {
                public TagsAttribute(params int[] values) => Count = values.Length;

                public int Count { get; }
            }

            [Tags(1, 2)]
            class C
            {
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task TypeParameterElementMutated_NoDiagnostic()
    {
        // T may be a struct, and on a ReadOnlySpan<T> the call would run on a defensive copy.
        var source = """
            interface ICounter
            {
                void Bump();
            }

            class C
            {
                static void BumpAll<T>(params T[] items) where T : ICounter
                {
                    for (var i = 0; i < items.Length; i++)
                        items[i].Bump();
                }
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>.VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task ReferenceTypeParameterElementCalled_Reports()
    {
        var source = """
            interface ICounter
            {
                void Bump();
            }

            class C
            {
                static void BumpAll<T>(params T[] {|#0:items|}) where T : class, ICounter
                {
                    for (var i = 0; i < items.Length; i++)
                        items[i].Bump();
                }
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("BumpAll", elementType: "T", parameter: "items"));
    }

    [Fact]
    public async Task Constructor_ReportsTypeName()
    {
        var source = """
            class C
            {
                C(params int[] {|#0:values|}) => Count = values.Length;

                int Count { get; }
            }
            """;

        await CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("C"));
    }

    [Fact]
    public async Task TwoDiagnosticsWithoutSystemImport_FixAllAddsOneImport()
    {
        var source = """
            class C
            {
                static int Sum(params int[] {|#0:values|}) => values.Length;

                static int Count(params string[] {|#1:names|}) => names.Length;
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                static int Sum(params ReadOnlySpan<int> values) => values.Length;

                static int Count(params ReadOnlySpan<string> names) => names.Length;
            }
            """;

        await CSharpCodeFixVerifier<PreferParamsSpanAnalyzer, PreferParamsSpanCodeFixProvider>
            .VerifyFixAllAsync(source, fixedSource,
            [
                Expected("Sum"),
                CSharpAnalyzerVerifier<PreferParamsSpanAnalyzer>
                    .Diagnostic(DiagnosticIds.PreferParamsSpan)
                    .WithLocation(1)
                    .WithArguments("names", "Count", "string", ""),
            ],
            compilationEndDiagnostic: true);
    }
}
