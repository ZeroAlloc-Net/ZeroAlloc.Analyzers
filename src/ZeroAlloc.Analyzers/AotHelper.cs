using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

internal static class AotHelper
{
    /// <summary>
    /// True when the project has opted into Native AOT or marked itself AOT-compatible.
    /// In that case the .NET SDK already enables the official AOT analyzer (IL3050+),
    /// so ZeroAlloc's ZA17xx rules stand down to avoid double-reporting.
    /// </summary>
    public static bool IsSdkAotAnalyzerEnabled(AnalyzerOptions options)
        => IsBuildPropertyTrue(options, "PublishAot")
            || IsBuildPropertyTrue(options, "IsAotCompatible")
            || IsBuildPropertyTrue(options, "EnableAotAnalyzer");

    /// <summary>
    /// True when the SDK's trim analyzer, which reports IL2026, is on. <c>EnableAotAnalyzer</c>
    /// alone turns on only the AOT analyzer (IL3050), so it is not counted here.
    /// </summary>
    public static bool IsSdkTrimAnalyzerEnabled(AnalyzerOptions options)
        => IsBuildPropertyTrue(options, "PublishAot")
            || IsBuildPropertyTrue(options, "IsAotCompatible")
            || IsBuildPropertyTrue(options, "EnableTrimAnalyzer")
            || IsBuildPropertyTrue(options, "PublishTrimmed")
            || IsBuildPropertyTrue(options, "IsTrimmable");

    /// <summary>True when the MSBuild property is visible to the analyzer and set to <c>true</c>.</summary>
    public static bool IsBuildPropertyTrue(AnalyzerOptions options, string name)
        => options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue($"build_property.{name}", out var value)
            && value.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the MSBuild property is visible to the analyzer and set to any non-empty value.</summary>
    public static bool IsBuildPropertySet(AnalyzerOptions options, string name)
        => options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue($"build_property.{name}", out var value)
            && !string.IsNullOrWhiteSpace(value);
}
