using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.CodeFixes;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0210_UseUtf8StringLiteralTests
{
    private static DiagnosticResult Expected(string literal) =>
        CSharpAnalyzerVerifier<UseUtf8StringLiteralAnalyzer>
            .Diagnostic(DiagnosticIds.UseUtf8StringLiteral)
            .WithLocation(0)
            .WithArguments(literal);

    [Fact]
    public async Task ConstantPassedAsSpan_ReportsAndFixes()
    {
        var source = """
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                void M() => Write({|#0:Encoding.UTF8.GetBytes("hello")|});
            }
            """;

        var fixedSource = """
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                void M() => Write("hello"u8);
            }
            """;

        await CSharpCodeFixVerifier<UseUtf8StringLiteralAnalyzer, UseUtf8StringLiteralCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("\"hello\""));
    }

    [Fact]
    public async Task ConstantAssignedToSpanLocal_ReportsAndFixes()
    {
        var source = """
            using System;
            using System.Text;

            class C
            {
                int M()
                {
                    ReadOnlySpan<byte> header = {|#0:Encoding.UTF8.GetBytes("GET ")|};
                    return header.Length;
                }
            }
            """;

        var fixedSource = """
            using System;
            using System.Text;

            class C
            {
                int M()
                {
                    ReadOnlySpan<byte> header = "GET "u8;
                    return header.Length;
                }
            }
            """;

        await CSharpCodeFixVerifier<UseUtf8StringLiteralAnalyzer, UseUtf8StringLiteralCodeFixProvider>
            .VerifyCodeFixAsync(source, fixedSource, Expected("\"GET \""));
    }

    [Fact]
    public async Task CSharp12_Reports()
    {
        var source = """
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                void M() => Write({|#0:Encoding.UTF8.GetBytes("hello")|});
            }
            """;

        await CSharpAnalyzerVerifier<UseUtf8StringLiteralAnalyzer>
            .VerifyAnalyzerAsync(source, LanguageVersion.CSharp12, Expected("\"hello\""));
    }

    [Fact]
    public async Task CSharp10_NoDiagnostic()
    {
        // u8 literals arrived in C# 11.
        var source = """
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                void M() => Write(Encoding.UTF8.GetBytes("hello"));
            }
            """;

        await CSharpAnalyzerVerifier<UseUtf8StringLiteralAnalyzer>
            .VerifyAnalyzerAsync(source, LanguageVersion.CSharp10);
    }

    [Theory]
    [InlineData("byte[] bytes = Encoding.UTF8.GetBytes(\"hello\"); Use(bytes);")]
    [InlineData("Write(Encoding.UTF8.GetBytes(name));")]
    [InlineData("Write(Encoding.ASCII.GetBytes(\"hello\"));")]
    [InlineData("Write(Encoding.UTF8.GetBytes(\"\\uD800\"));")]
    public async Task NotADropInReplacement_NoDiagnostic(string statement)
    {
        // A byte[] result, a non-constant, another encoding, or text with a lone surrogate.
        var source = $$"""
            using System;
            using System.Text;

            class C
            {
                static void Write(ReadOnlySpan<byte> bytes) { }

                static void Use(byte[] bytes) { }

                void M(string name)
                {
                    {{statement}}
                }
            }
            """;

        await CSharpAnalyzerVerifier<UseUtf8StringLiteralAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }
}
