using Microsoft.CodeAnalysis.Testing;
using ZeroAlloc.Analyzers.Tests.Verifiers;

namespace ZeroAlloc.Analyzers.Tests;

public class ZA1503_ImplementEquatableOnStructKeyTests
{
    private static DiagnosticResult Expected(string type = "Point") =>
        CSharpAnalyzerVerifier<ImplementEquatableOnStructKeyAnalyzer>
            .Diagnostic(DiagnosticIds.ImplementEquatableOnStructKey)
            .WithLocation(0)
            .WithArguments(type);

    private static string WithKeys(string statement) => $$"""
        using System;
        using System.Collections.Concurrent;
        using System.Collections.Frozen;
        using System.Collections.Generic;
        using System.Linq;

        struct Point
        {
            public int X;
            public int Y;
        }

        struct EquatablePoint : IEquatable<EquatablePoint>
        {
            public int X;
            public bool Equals(EquatablePoint other) => X == other.X;
            public override int GetHashCode() => X;
        }

        record struct RecordPoint(int X);

        enum Color { Red }

        class Box { }

        sealed class PointComparer : IEqualityComparer<Point>
        {
            public static readonly PointComparer Instance = new();
            public bool Equals(Point a, Point b) => a.X == b.X && a.Y == b.Y;
            public int GetHashCode(Point p) => p.X;
        }

        class C
        {
            object M(Point[] points)
            {
                {{statement}}
            }
        }
        """;

    [Theory]
    [InlineData("return {|#0:new Dictionary<Point, int>()|};")]
    [InlineData("return {|#0:new HashSet<Point>()|};")]
    [InlineData("return {|#0:new ConcurrentDictionary<Point, int>()|};")]
    [InlineData("return {|#0:new Dictionary<Point, int>(new Dictionary<Point, int>(PointComparer.Instance))|};")]
    [InlineData("return {|#0:points.ToDictionary(p => p, p => 0)|};")]
    [InlineData("return {|#0:points.ToHashSet()|};")]
    [InlineData("return {|#0:points.ToFrozenSet()|};")]
    [InlineData("return {|#0:points.Select(p => new KeyValuePair<Point, int>(p, 0)).ToFrozenDictionary()|};")]
    [InlineData("return {|#0:points.ToFrozenSet(null)|};")]
    [InlineData("return {|#0:new HashSet<Point>((IEqualityComparer<Point>)null)|};")]
    [InlineData("return {|#0:points.ToDictionary(p => p, p => 0, null)|};")]
    [InlineData("return {|#0:new Dictionary<Point, int>(default(IEqualityComparer<Point>))|};")]
    [InlineData("return {|#0:new Dictionary<Point?, int>()|};")]
    [InlineData("return {|#0:new Dictionary<Point, int>(EqualityComparer<Point>.Default)|};")]
    [InlineData("return {|#0:points.ToHashSet(EqualityComparer<Point>.Default)|};")]
    [InlineData("return {|#0:new HashSet<Point>((IEqualityComparer<Point>)EqualityComparer<Point>.Default)|};")]
    public async Task StructKeyWithoutEquatable_Reports(string statement)
    {
        await CSharpAnalyzerVerifier<ImplementEquatableOnStructKeyAnalyzer>
            .VerifyAnalyzerAsync(WithKeys(statement), "net8.0", Expected());
    }

    [Theory]
    [InlineData("return new Dictionary<EquatablePoint, int>();")]
    [InlineData("return new HashSet<RecordPoint>();")]
    [InlineData("return new Dictionary<Color, int>();")]
    [InlineData("return new Dictionary<EquatablePoint?, int>();")]
    [InlineData("return new Dictionary<Box, int>();")]
    [InlineData("return new Dictionary<Point, int>(PointComparer.Instance);")]
    [InlineData("return new HashSet<Point>(points, PointComparer.Instance);")]
    [InlineData("return points.ToDictionary(p => p, p => 0, PointComparer.Instance);")]
    [InlineData("return points.ToHashSet(PointComparer.Instance);")]
    [InlineData("return points.ToFrozenSet(PointComparer.Instance);")]
    [InlineData("return new Dictionary<KeyValuePair<int, int>, int>();")]
    public async Task KeyIsFineOrComparerPassed_NoDiagnostic(string statement)
    {
        // Equatable, record and enum keys, reference keys, an explicit comparer, and a struct
        // from another assembly that the user cannot change.
        await CSharpAnalyzerVerifier<ImplementEquatableOnStructKeyAnalyzer>
            .VerifyNoDiagnosticAsync(WithKeys(statement), "net8.0");
    }
}
