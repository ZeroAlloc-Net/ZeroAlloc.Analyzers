using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0501_AvoidBoxingInLoopsTests
{
    [Fact]
    public async Task IntPassedToObjectParam_InLoop_Reports()
    {
        var source = """
            class C
            {
                static void Log(object value) { }

                void M()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Log({|#0:i|});
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidBoxingInLoops)
            .WithLocation(0)
            .WithArguments("int", "object");

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task StructPassedToInterfaceParam_InLoop_Reports()
    {
        var source = """
            using System;

            class C
            {
                static void Format(IFormattable value) { }

                void M()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Format({|#0:i|});
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidBoxingInLoops)
            .WithLocation(0)
            .WithArguments("int", "IFormattable");

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task IntPassedToObjectParam_OutsideLoop_NoDiagnostic()
    {
        var source = """
            class C
            {
                static void Log(object value) { }

                void M()
                {
                    Log(42);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task StringPassedToObjectParam_InLoop_NoDiagnostic()
    {
        var source = """
            class C
            {
                static void Log(object value) { }

                void M()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Log("hello");
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task IntPassedToGenericParam_InLoop_NoDiagnostic()
    {
        var source = """
            class C
            {
                static void Log<T>(T value) { }

                void M()
                {
                    for (int i = 0; i < 10; i++)
                    {
                        Log(i);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task EnumPassedToObjectParam_InLoop_Reports()
    {
        var source = """
            using System;

            enum Color { Red, Green, Blue }

            class C
            {
                static void Log(object value) { }

                void M()
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Log({|#0:(Color)i|});
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidBoxingInLoops)
            .WithLocation(0)
            .WithArguments("Color", "object");

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LoopHeaderForeachCollection_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                static object[] W(object o) => null!;
                static bool B(object o) => true;

                void M(int n)
                {
                    foreach (var o in W(n))
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForeachCollectionInsideOuterLoop_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                static object[] W(object o) => null!;
                static bool B(object o) => true;

                void M(int n)
                {
                    for (int j = 0; j < 10; j++)
                    {
                        foreach (var o in W({|#0:n|}))
                        {
                        }
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidBoxingInLoops)
            .WithLocation(0)
            .WithArguments("int", "object");

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LoopHeaderForDeclaration_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                static object[] W(object o) => null!;
                static bool B(object o) => true;

                void M(int n)
                {
                    for (var a = W(n); a == null; n++)
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForInitializer_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                static object[] W(object o) => null!;
                static bool B(object o) => true;

                void M(int n)
                {
                    for (W(n); n < 0; n++)
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForCondition_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                static object[] W(object o) => null!;
                static bool B(object o) => true;

                void M(int n)
                {
                    for (int i = 0; B({|#0:i|}); i++)
                    {
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidBoxingInLoops)
            .WithLocation(0)
            .WithArguments("int", "object");

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task DeconstructingForeachBody_Reports()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                static object[] W(object o) => null!;
                static bool B(object o) => true;

                void M(int n, Dictionary<int, int> map)
                {
                    foreach (var (k, v) in map)
                    {
                        W({|#0:k|});
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidBoxingInLoops)
            .WithLocation(0)
            .WithArguments("int", "object");

        await CSharpAnalyzerVerifier<AvoidBoxingInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }
}
