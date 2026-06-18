using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public static class SsmsLaunchSettingsValidator
{
    private const string NoSplashArgument = "-nosplash";

    public static bool TryNormalizeExecutablePath(string? executablePath, out string? normalizedPath, out string? errorMessage)
    {
        normalizedPath = null;
        errorMessage = null;

        string? trimmedPath = ValueNormalization.EmptyToNull(executablePath);
        if (trimmedPath is null)
        {
            return true;
        }

        if (IsNetworkOrDevicePath(trimmedPath))
        {
            errorMessage = "Choose a local SSMS executable. Network and device paths are not allowed.";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(trimmedPath))
            {
                errorMessage = "Choose a fully qualified SSMS executable path.";
                return false;
            }

            string fullPath = Path.GetFullPath(trimmedPath);
            if (IsNetworkOrDevicePath(fullPath))
            {
                errorMessage = "Choose a local SSMS executable. Network and device paths are not allowed.";
                return false;
            }

            if (!string.Equals(Path.GetFileName(fullPath), SsmsPaths.ExecutableFileName, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Choose {SsmsPaths.ExecutableFileName}. Other executables cannot be launched from this setting.";
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            errorMessage = "Choose a valid fully qualified SSMS executable path.";
            return false;
        }
    }

    public static bool TryValidateExecutablePathForLaunch(
        string? executablePath,
        IEnumerable<SsmsInstance> detectedInstances,
        out string normalizedPath,
        out string errorMessage)
    {
        normalizedPath = string.Empty;
        errorMessage = string.Empty;

        if (!TryNormalizeExecutablePath(executablePath, out string? normalizedExecutablePath, out string? normalizationError)
            || normalizedExecutablePath is null)
        {
            errorMessage = normalizationError
                ?? $"The SSMS executable path is not set. Open Settings to choose the {SsmsPaths.ExecutableFileName} start location.";
            return false;
        }

        if (!File.Exists(normalizedExecutablePath))
        {
            errorMessage = $"The configured SSMS executable was not found:\n\n{normalizedExecutablePath}\n\nOpen Settings to update the start location.";
            return false;
        }

        string resolvedExecutablePath = ResolveExistingPath(normalizedExecutablePath);
        foreach (SsmsInstance instance in detectedInstances)
        {
            string expectedExecutablePath = SsmsPaths.GetExecutablePath(instance.InstallationPath);
            if (!File.Exists(expectedExecutablePath))
            {
                continue;
            }

            string resolvedExpectedPath = ResolveExistingPath(expectedExecutablePath);
            if (string.Equals(resolvedExecutablePath, resolvedExpectedPath, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = resolvedExpectedPath;
                return true;
            }
        }

        errorMessage = $"Choose the detected {SsmsPaths.ExecutableFileName} under an SSMS 22 installation. Other local executables cannot be launched from this setting.";
        return false;
    }

    public static bool TryNormalizeArguments(string? arguments, out string normalizedArguments, out string? errorMessage)
    {
        normalizedArguments = string.Empty;
        errorMessage = null;

        string? trimmedArguments = ValueNormalization.EmptyToNull(arguments);
        if (trimmedArguments is null)
        {
            return true;
        }

        string[] tokens = trimmedArguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 1
            && (string.Equals(tokens[0], NoSplashArgument, StringComparison.OrdinalIgnoreCase)
                || string.Equals(tokens[0], "/nosplash", StringComparison.OrdinalIgnoreCase)))
        {
            normalizedArguments = NoSplashArgument;
            return true;
        }

        errorMessage = $"Only the {NoSplashArgument} launch argument is allowed.";
        return false;
    }

    public static IReadOnlyList<string> ToArgumentList(string normalizedArguments)
        => string.IsNullOrWhiteSpace(normalizedArguments)
            ? []
            : [normalizedArguments];

    private static bool IsNetworkOrDevicePath(string path)
        => path.StartsWith(@"\\", StringComparison.Ordinal)
            || path.StartsWith("//", StringComparison.Ordinal);

    private static string ResolveExistingPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        try
        {
            FileSystemInfo? target = new FileInfo(fullPath).ResolveLinkTarget(returnFinalTarget: true);
            if (target is not null)
            {
                return Path.GetFullPath(target.FullName);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return fullPath;
    }
}
