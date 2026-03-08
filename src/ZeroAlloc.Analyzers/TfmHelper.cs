using Microsoft.CodeAnalysis.Diagnostics;

namespace ZeroAlloc.Analyzers;

internal static class TfmHelper
{
    public static bool TryGetTfm(AnalyzerOptions options, out string tfm)
    {
        if (options.AnalyzerConfigOptionsProvider
            .GlobalOptions
            .TryGetValue("build_property.TargetFramework", out var value)
            && !string.IsNullOrEmpty(value))
        {
            tfm = value;
            return true;
        }

        tfm = string.Empty;
        return false;
    }

    public static bool IsNetOrLater(string tfm, int majorVersion)
    {
        // Handles: net5.0, net6.0, net7.0, net8.0, net9.0, net10.0, etc.
        // Excludes .NET Framework TFMs (net48, net472) which lack a dot after the version.
        if (tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase)
            && !tfm.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
            && !tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
        {
            var versionPart = tfm.Substring(3);
            var dotIndex = versionPart.IndexOf('.');
            // .NET Framework TFMs (net48, net472) have no dot — reject them
            if (dotIndex <= 0)
                return false;
            versionPart = versionPart.Substring(0, dotIndex);
            // Also strip any suffix like -windows
            var dashIndex = versionPart.IndexOf('-');
            if (dashIndex > 0)
                versionPart = versionPart.Substring(0, dashIndex);

            if (int.TryParse(versionPart, out var major))
                return major >= majorVersion;
        }

        // netcoreapp3.1 etc. — treat as < net5
        return false;
    }

    public static bool IsNet5OrLater(string tfm) => IsNetOrLater(tfm, 5);
    public static bool IsNet6OrLater(string tfm) => IsNetOrLater(tfm, 6);
    public static bool IsNet8OrLater(string tfm) => IsNetOrLater(tfm, 8);
}
