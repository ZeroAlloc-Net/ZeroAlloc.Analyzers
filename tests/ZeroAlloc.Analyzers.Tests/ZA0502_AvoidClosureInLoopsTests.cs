using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0502_AvoidClosureInLoopsTests
{
    [Fact]
    public async Task LambdaCapturingLoopVariable_Reports()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var result = items.Where(x {|#0:=>|} x > i);
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidClosureInLoops)
            .WithLocation(0)
            .WithArguments("i");

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LambdaCapturingOuterVariable_InLoop_Reports()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    int threshold = 5;
                    for (int i = 0; i < 10; i++)
                    {
                        var result = items.Where(x {|#0:=>|} x > threshold);
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidClosureInLoops)
            .WithLocation(0)
            .WithArguments("threshold");

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LambdaWithNoCapture_InLoop_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var result = items.Where(x => x > 0);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LambdaCapturingVariable_OutsideLoop_NoDiagnostic()
    {
        var source = """
            using System;
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    int threshold = 5;
                    var result = items.Where(x => x > threshold);
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForeachCollection_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items, int n)
                {
                    foreach (var v in items.Where(x => x > n))
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForeachCollectionInsideOuterLoop_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items, int n)
                {
                    for (int j = 0; j < 10; j++)
                    {
                        foreach (var v in items.Where(x {|#0:=>|} x > n))
                        {
                        }
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidClosureInLoops)
            .WithLocation(0)
            .WithArguments("n");

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LoopHeaderForDeclaration_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items, int n)
                {
                    for (var q = items.Where(x => x > n); q == null; n++)
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForInitializer_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items, int n)
                {
                    for (items.Count(x => x > n); n < 0; n++)
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LoopHeaderForCondition_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items, int n)
                {
                    for (int i = 0; items.Any(x {|#0:=>|} x > n); i++)
                    {
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidClosureInLoops)
            .WithLocation(0)
            .WithArguments("n");

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task DeconstructingForeachBody_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items, int n, Dictionary<int, int> map)
                {
                    foreach (var (k, v) in map)
                    {
                        items.Where(x {|#0:=>|} x > n);
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidClosureInLoops)
            .WithLocation(0)
            .WithArguments("n");

        await CSharpAnalyzerVerifier<AvoidClosureInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }
}
