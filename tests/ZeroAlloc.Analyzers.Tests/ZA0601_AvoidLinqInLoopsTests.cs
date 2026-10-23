using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA0601_AvoidLinqInLoopsTests
{
    [Fact]
    public async Task ToListInForLoop_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var copy = items.{|#0:ToList|}();
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("ToList");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task WhereInForeachLoop_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    foreach (var item in items)
                    {
                        var filtered = items.{|#0:Where|}(x => x > 0);
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("Where");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LinqOutsideLoop_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    var copy = items.ToList();
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task NonLinqMethodInLoop_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;

            class C
            {
                void M(List<int> items)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        items.Add(i);
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LinqAsForeachCollectionExpression_NoDiagnostic()
    {
        // The collection expression of a foreach is evaluated once, before the first iteration.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    foreach (var item in items.OrderBy(x => x).ThenBy(x => -x))
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LinqAsDeconstructingForeachCollectionExpression_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(Dictionary<string, int> map)
                {
                    foreach (var (key, value) in map.OrderBy(p => p.Key))
                    {
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LinqAsForeachCollectionExpressionInsideOuterLoop_Reports()
    {
        // The inner foreach's collection expression runs once per iteration of the outer loop.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        foreach (var item in items.{|#0:Where|}(x => x > i))
                        {
                        }
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("Where");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LinqAsForeachCollectionExpressionInsideOuterForeachBody_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<List<int>> groups)
                {
                    foreach (var group in groups)
                    {
                        foreach (var item in group.{|#0:OrderBy|}(x => x))
                        {
                        }
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("OrderBy");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LinqInDeconstructingForeachBody_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(Dictionary<string, int> map, List<int> items)
                {
                    foreach (var (key, value) in map)
                    {
                        var any = items.{|#0:Any|}(x => x == value);
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("Any");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LinqInForInitializer_NoDiagnostic()
    {
        // The declaration and initializers of a for statement run once.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(IEnumerable<int> items)
                {
                    for (var snapshot = items.ToArray(); snapshot.Length > 0; )
                    {
                        break;
                    }

                    int[] copy;
                    for (copy = items.ToArray(); copy.Length > 0; )
                    {
                        break;
                    }
                }
            }
            """;

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyNoDiagnosticAsync(source, "net8.0");
    }

    [Fact]
    public async Task LinqInForCondition_Reports()
    {
        // The condition of a for statement is evaluated on every iteration.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    for (int i = 0; i < items.{|#0:Count|}(); i++)
                    {
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("Count");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LinqInForIncrementer_Reports()
    {
        // The incrementers of a for statement run on every iteration.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    for (int i = 0; i < 10; i += items.{|#0:Sum|}())
                    {
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("Sum");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LinqInWhileCondition_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    while (items.{|#0:Any|}())
                    {
                        items.RemoveAt(0);
                    }
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("Any");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }

    [Fact]
    public async Task LinqInDoWhileCondition_Reports()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            class C
            {
                void M(List<int> items)
                {
                    do
                    {
                        items.RemoveAt(0);
                    }
                    while (items.{|#0:Any|}());
                }
            }
            """;

        var expected = CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .Diagnostic(DiagnosticIds.AvoidLinqInLoops)
            .WithLocation(0)
            .WithArguments("Any");

        await CSharpAnalyzerVerifier<AvoidLinqInLoopsAnalyzer>
            .VerifyAnalyzerAsync(source, "net8.0", expected);
    }
}
