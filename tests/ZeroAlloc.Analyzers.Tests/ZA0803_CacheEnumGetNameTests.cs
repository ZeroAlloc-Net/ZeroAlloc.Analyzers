using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0803_CacheEnumGetNameTests
{
    [Fact]
    public async Task EnumGetNameInLoop_Reports()
    {
        var source = """
            using System;

            class C
            {
                enum Color { Red, Green, Blue }

                void M()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var name = {|#0:Enum.GetName(typeof(Color), i)|};
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .Diagnostic(DiagnosticIds.CacheEnumGetName)
            .WithLocation(0)
            .WithArguments("Enum.GetName");

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task EnumGetValuesInLoop_Reports()
    {
        var source = """
            using System;

            class C
            {
                enum Color { Red, Green, Blue }

                void M()
                {
                    while (true)
                    {
                        var values = {|#0:Enum.GetValues(typeof(Color))|};
                        break;
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .Diagnostic(DiagnosticIds.CacheEnumGetName)
            .WithLocation(0)
            .WithArguments("Enum.GetValues");

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task EnumGetNameOutsideLoop_NoDiagnostic()
    {
        var source = """
            using System;

            class C
            {
                enum Color { Red, Green, Blue }

                void M()
                {
                    var name = Enum.GetName(typeof(Color), 0);
                }
            }
            """;

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task EnumGetValuesOutsideLoop_NoDiagnostic()
    {
        var source = """
            using System;

            class C
            {
                enum Color { Red, Green, Blue }

                void M()
                {
                    var values = Enum.GetValues(typeof(Color));
                }
            }
            """;

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForeachCollection_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System;

            class C
            {
                enum Color { Red, Green }

                void M(int n)
                {
                    foreach (var v in Enum.GetValues(typeof(Color)))
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForeachCollectionInsideOuterLoop_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System;

            class C
            {
                enum Color { Red, Green }

                void M(int n)
                {
                    for (int j = 0; j < 10; j++)
                    {
                        foreach (var v in {|#0:Enum.GetValues(typeof(Color))|})
                        {
                        }
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .Diagnostic(DiagnosticIds.CacheEnumGetName)
            .WithLocation(0)
            .WithArguments("Enum.GetValues");

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LoopHeaderForDeclaration_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System;

            class C
            {
                enum Color { Red, Green }

                void M(int n)
                {
                    for (var a = Enum.GetValues(typeof(Color)); a == null; n++)
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForInitializer_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System;

            class C
            {
                enum Color { Red, Green }

                void M(int n)
                {
                    for (Enum.GetValues(typeof(Color)); n < 0; n++)
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForCondition_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System;

            class C
            {
                enum Color { Red, Green }

                void M(int n)
                {
                    for (int i = 0; {|#0:Enum.GetValues(typeof(Color))|}.Length > i; i++)
                    {
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .Diagnostic(DiagnosticIds.CacheEnumGetName)
            .WithLocation(0)
            .WithArguments("Enum.GetValues");

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task DeconstructingForeachBody_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System;

            class C
            {
                enum Color { Red, Green }

                void M(int n, Dictionary<int, int> map)
                {
                    foreach (var (k, v) in map)
                    {
                        {|#0:Enum.GetValues(typeof(Color))|};
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .Diagnostic(DiagnosticIds.CacheEnumGetName)
            .WithLocation(0)
            .WithArguments("Enum.GetValues");

        await CSharpAnalyzerVerifier<CacheEnumGetNameAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }
}
