using System.Text.RegularExpressions;

namespace SsmsExtensionManager.Core.Services;

public static partial class VersionComparer
{
    public static bool IsNewer(string candidate, string installed)
    {
        if (TryNormalize(candidate, out Version? candidateVersion) && TryNormalize(installed, out Version? installedVersion))
        {
            return candidateVersion > installedVersion;
        }

        return string.Compare(candidate, installed, StringComparison.OrdinalIgnoreCase) > 0;
    }

    public static string? ExtractVersionText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = VersionTextPattern().Match(value).Value;
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static bool TryNormalize(string value, out Version? version)
    {
        version = null;
        string normalized = ExtractVersionText(value) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalized) && Version.TryParse(normalized, out version);
    }

    [GeneratedRegex(@"\d+(\.\d+){0,3}", RegexOptions.Compiled)]
    private static partial Regex VersionTextPattern();
}
