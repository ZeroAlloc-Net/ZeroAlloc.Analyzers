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
}
