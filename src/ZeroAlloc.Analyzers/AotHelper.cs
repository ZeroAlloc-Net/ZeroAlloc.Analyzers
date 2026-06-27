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
    {
        var global = options.AnalyzerConfigOptionsProvider.GlobalOptions;
        return IsTrue(global, "build_property.PublishAot")
            || IsTrue(global, "build_property.IsAotCompatible")
            || IsTrue(global, "build_property.EnableAotAnalyzer");
    }

    private static bool IsTrue(AnalyzerConfigOptions options, string key)
        => options.TryGetValue(key, out var value)
            && value.Equals("true", StringComparison.OrdinalIgnoreCase);
}
