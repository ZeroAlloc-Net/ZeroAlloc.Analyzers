using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0304_UseReadOnlySpanForConstantTableTests
{
    private static DiagnosticResult Expected(string field, string elementType) =>
        CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .Diagnostic(DiagnosticIds.UseReadOnlySpanForConstantTable)
            .WithLocation(0)
            .WithArguments(field, elementType);

    [Fact]
    public async Task ByteTable_ReportsAndFixes()
    {
        var source = """
            using System;

            class C
            {
                private static readonly byte[] {|#0:Table|} = { 1, 2, 3 };

                int M(int i) => Table[i] + Table.Length;
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                private static ReadOnlySpan<byte> Table => [1, 2, 3];

                int M(int i) => Table[i] + Table.Length;
            }
            """;

        await CSharpCodeFixVerifier<UseReadOnlySpanForConstantTableAnalyzer, UseReadOnlySpanForConstantTableCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Table", "byte"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task IntTableWithArrayCreationAndForeach_ReportsAndFixes()
    {
        var source = """
            using System;

            class C
            {
                internal static readonly int[] {|#0:Primes|} = new int[] { 2, 3, 5 };

                int Sum()
                {
                    var total = 0;
                    foreach (var prime in Primes)
                        total += prime;
                    return total;
                }
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                internal static ReadOnlySpan<int> Primes => [2, 3, 5];

                int Sum()
                {
                    var total = 0;
                    foreach (var prime in Primes)
                        total += prime;
                    return total;
                }
            }
            """;

        await CSharpCodeFixVerifier<UseReadOnlySpanForConstantTableAnalyzer, UseReadOnlySpanForConstantTableCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Primes", "int"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task IntTableOnNet6_NoDiagnostic()
    {
        // Wider primitives come from static data only once RuntimeHelpers.CreateSpan exists, in .NET 7.
        var source = """
            class C
            {
                private static readonly int[] Table = { 1, 2, 3 };

                int M(int i) => Table[i];
            }
            """;

        await CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net6.0", Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net60);
    }

    [Fact]
    public async Task ByteTableOnNet6_Reports()
    {
        var source = """
            class C
            {
                private static readonly byte[] {|#0:Table|} = { 1, 2, 3 };

                int M(int i) => Table[i];
            }
            """;

        await CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .VerifyAnalyzerAsync(source, "net6.0", Microsoft.CodeAnalysis.Testing.ReferenceAssemblies.Net.Net60, Expected("Table", "byte"));
    }

    [Theory]
    [InlineData("private static readonly string[] Table = { \"a\" };", "int M() => Table.Length;")]
    [InlineData("public static readonly byte[] Table = { 1 };", "int M() => Table.Length;")]
    [InlineData("private static readonly byte[] Table = { 1, Seed };", "int M() => Table.Length;")]
    [InlineData("private static readonly byte[] Table = { 1 };", "void M() => Table[0] = 2;")]
    [InlineData("private static readonly byte[] Table = { 1 };", "void M() => Use(Table);")]
    [InlineData("private static readonly byte[] Table = { 1 };", "int M() => Table.ToList().Count;")]
    [InlineData("private static readonly byte[] Table = { 1 };", "void M() => Table[0]++;")]
    [InlineData("private static readonly byte[] Table = { 1 };", "async System.Threading.Tasks.Task<int> M() { await System.Threading.Tasks.Task.Yield(); var t = 0; foreach (var b in Table) t += b; return t; }")]
    [InlineData("private static readonly byte[] Table = { 1 };", "System.Collections.Generic.IEnumerable<int> M() { foreach (var b in Table) yield return b; }")]
    [InlineData("private static readonly byte[] Table = { 1 };", "System.Collections.Generic.IEnumerable<int> M() { System.Collections.Generic.IEnumerable<int> Local() { foreach (var b in Table) yield return b; } return Local(); }")]
    [InlineData("private static readonly byte[] Table = { 1 };", "System.Linq.Expressions.Expression<System.Func<int, int>> M() => i => Table[i];")]
    [InlineData("private static readonly byte[] Table = { 1 }, Other = { 2 };", "int M() => Table.Length + Other.Length;")]
    public async Task NotAConstantReadOnlyTable_NoDiagnostic(string field, string member)
    {
        // Non-primitive elements, exposed fields, non-constant elements, writes, passing the
        // array on, LINQ, and multi-variable declarations all keep the array.
        var source = $$"""
            using System.Linq;

            class C
            {
                private static readonly byte Seed = 2;

                {{field}}

                private static void Use(byte[] array) { }

                {{member}}
            }
            """;

        await CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task BoolTable_Reports()
    {
        var source = """
            class C
            {
                private static readonly bool[] {|#0:Flags|} = { true, false };

                bool M(int i) => Flags[i];
            }
            """;

        await CSharpAnalyzerVerifier<UseReadOnlySpanForConstantTableAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", Expected("Flags", "bool"));
    }

    [Fact]
    public async Task UseInGeneratedFile_NoDiagnostic()
    {
        var test = NewAnalyzerTest("""
            partial class C
            {
                private static readonly byte[] Table = { 1, 2 };

                int M(int i) => Table[i];
            }
            """);
        test.TestState.Sources.Add(("Generated.g.cs", """
            // <auto-generated/>
            partial class C
            {
                private static void Use(byte[] array) { }

                void G() => Use(Table);
            }
            """));

        await test.RunAsync();
    }

    [Fact]
    public async Task InternalTableWithFriendAssembly_NoDiagnostic()
    {
        var test = NewAnalyzerTest("""
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Other")]

            class C
            {
                internal static readonly byte[] Table = { 1, 2 };

                int M(int i) => Table[i];
            }
            """);

        await test.RunAsync();
    }

    [Fact]
    public async Task PrivateTableWithFriendAssembly_Reports()
    {
        var test = NewAnalyzerTest("""
            [assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Other")]

            class C
            {
                private static readonly byte[] {|#0:Table|} = { 1, 2 };

                int M(int i) => Table[i];
            }
            """);
        test.ExpectedDiagnostics.Add(Expected("Table", "byte"));

        await test.RunAsync();
    }

    [Fact]
    public async Task TableWithoutSystemUsing_FixAddsUsing()
    {
        var source = """
            class C
            {
                private static readonly byte[] {|#0:Table|} = { 1, 2, 3 };

                int M(int i) => Table[i];
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                private static ReadOnlySpan<byte> Table => [1, 2, 3];

                int M(int i) => Table[i];
            }
            """;

        await CSharpCodeFixVerifier<UseReadOnlySpanForConstantTableAnalyzer, UseReadOnlySpanForConstantTableCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Table", "byte"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task TableOnCSharp10_FixEmitsArrayCreation()
    {
        var source = """
            using System;

            class C
            {
                private static readonly byte[] {|#0:Table|} = { 1, 2, 3 };

                int M(int i) => Table[i];
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                private static ReadOnlySpan<byte> Table => new byte[] { 1, 2, 3 };

                int M(int i) => Table[i];
            }
            """;

        await CSharpCodeFixVerifier<UseReadOnlySpanForConstantTableAnalyzer, UseReadOnlySpanForConstantTableCodeFixProvider>
            .VerifyCodeFixAsync(
                source,
                fixedSource,
                Expected("Table", "byte"),
                "net6.0",
                compilationEndDiagnostic: true,
                languageVersion: Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp10,
                referenceAssemblies: ReferenceAssemblies.Net.Net60);
    }

    [Fact]
    public async Task MultiLineTableWithComments_FixKeepsThem()
    {
        var source = """
            using System;

            class C
            {
                private static readonly byte[] {|#0:Table|} =
                {
                    1, 2, // first row
                    3, 4, // second row
                };

                int M(int i) => Table[i];
            }
            """;

        var fixedSource = """
            using System;

            class C
            {
                private static ReadOnlySpan<byte> Table => [
                    1, 2, // first row
                    3, 4, // second row
                ];

                int M(int i) => Table[i];
            }
            """;

        await CSharpCodeFixVerifier<UseReadOnlySpanForConstantTableAnalyzer, UseReadOnlySpanForConstantTableCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Table", "byte"), compilationEndDiagnostic: true);
    }

    [Fact]
    public async Task NullableArrayTable_FixDropsNullability()
    {
        var source = """
            #nullable enable
            using System;

            class C
            {
                private static readonly byte[]? {|#0:Table|} = { 1, 2 };

                int M() => Table!.Length;
            }
            """;

        var fixedSource = """
            #nullable enable
            using System;

            class C
            {
                private static ReadOnlySpan<byte> Table => [1, 2];

                int M() => Table!.Length;
            }
            """;

        await CSharpCodeFixVerifier<UseReadOnlySpanForConstantTableAnalyzer, UseReadOnlySpanForConstantTableCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("Table", "byte"), compilationEndDiagnostic: true);
    }

    private static CSharpAnalyzerTest<UseReadOnlySpanForConstantTableAnalyzer, DefaultVerifier> NewAnalyzerTest(string source)
    {
        var test = new CSharpAnalyzerTest<UseReadOnlySpanForConstantTableAnalyzer, DefaultVerifier>
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
}
