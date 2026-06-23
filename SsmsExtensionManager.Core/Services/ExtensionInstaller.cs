using System.Diagnostics;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class ExtensionInstaller(ExtensionAssetResolver assetResolver)
{
    public OperationResult InstallLocalAsset(SsmsInstance instance, string assetPath, CancellationToken cancellationToken = default)
    {
        ExtensionAsset asset;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            asset = assetResolver.Resolve(assetPath, AppPaths.TempAssetsRoot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        VsixInstallerResult installerResult = RunVsixInstaller(instance, BuildInstallArguments(asset.FilePath), cancellationToken);

        if (!installerResult.Success && IsAlreadyInstalledFailure(installerResult.ExitCode, installerResult.LogText))
            return OperationResult.Fail($"{asset.Manifest.DisplayName} {asset.Manifest.Version} is already installed.");

        return installerResult.Success
            ? OperationResult.Ok($"Install complete for {asset.Manifest.DisplayName} {asset.Manifest.Version}.")
            : OperationResult.Fail(installerResult.Message);
    }

    public OperationResult UpdateInstalledExtension(InstalledExtension installedExtension, string assetPath, CancellationToken cancellationToken = default)
    {
        ExtensionAsset asset;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            asset = assetResolver.Resolve(assetPath, AppPaths.TempAssetsRoot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        if (!VsixUpdateIdentityPolicy.TryValidateUpdate(installedExtension.Manifest, asset.Manifest, out string identityError))
        {
            return OperationResult.Fail(identityError);
        }

        VsixInstallerResult installerResult = RunVsixInstaller(installedExtension.SsmsInstance, BuildInstallArguments(asset.FilePath), cancellationToken);

        return installerResult.Success
            ? OperationResult.Ok($"Update complete for {asset.Manifest.DisplayName} {asset.Manifest.Version}.")
            : OperationResult.Fail(installerResult.Message);
    }

    public OperationResult Uninstall(InstalledExtension extension, CancellationToken cancellationToken = default)
    {
        VsixInstallerResult installerResult = RunVsixInstaller(extension.SsmsInstance, BuildUninstallArguments(extension.Manifest.Id), cancellationToken);
        return installerResult.Success
            ? OperationResult.Ok($"Uninstall complete for {extension.Manifest.DisplayName}.")
            : OperationResult.Fail(installerResult.Message);
    }

    private static VsixInstallerResult RunVsixInstaller(SsmsInstance instance, string arguments, CancellationToken cancellationToken)
    {
        string installerPath = SsmsPaths.GetVsixInstallerPath(instance.InstallationPath);
        if (!File.Exists(installerPath))
        {
            return VsixInstallerResult.Fail(-1, string.Empty, $"{SsmsPaths.VsixInstallerFileName} was not found at {installerPath}.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = arguments
            }
        };

        DateTime logSearchStartUtc = DateTime.UtcNow;
        try
        {
            process.Start();
            using CancellationTokenRegistration registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // The process may have exited between the cancellation request and kill attempt.
                }
            });
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                throw;
            }

            return VsixInstallerResult.Fail(-1, string.Empty, ex.Message);
        }

        string logText = ReadRecentVsixInstallerLogs(logSearchStartUtc);
        if (process.ExitCode != 0)
        {
            string detail = ExtractFailureDetail(logText);
            return VsixInstallerResult.Fail(process.ExitCode, logText, string.IsNullOrWhiteSpace(detail)
                ? $"VSIXInstaller exited with code {process.ExitCode}."
                : $"VSIXInstaller exited with code {process.ExitCode}: {detail}");
        }

        return VsixInstallerResult.Ok(process.ExitCode, logText);
    }

    internal static string BuildInstallArguments(string assetPath) => $"/quiet {QuoteArgument(assetPath)}";

    private static string BuildUninstallArguments(string extensionId) => $"/quiet /u:{extensionId}";

    private static bool IsAlreadyInstalledFailure(int exitCode, string logText)
        => exitCode == 1001
            || logText.Contains("AlreadyInstalledException", StringComparison.OrdinalIgnoreCase)
            || logText.Contains("already installed", StringComparison.OrdinalIgnoreCase);

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string ReadRecentVsixInstallerLogs(DateTime startUtc)
    {
        try
        {
            DirectoryInfo tempDirectory = new(Path.GetTempPath());
            return string.Join(
                Environment.NewLine,
                tempDirectory
                    .EnumerateFiles("dd_VSIXInstaller_*.log", SearchOption.TopDirectoryOnly)
                    .Where(file => file.LastWriteTimeUtc >= startUtc && file.Length > 0)
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Take(4)
                    .Select(ReadLogBestEffort)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ReadLogBestEffort(FileInfo file)
    {
        try
        {
            return File.ReadAllText(file.FullName);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ExtractFailureDetail(string logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return string.Empty;
        }

        string? line = logText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(line =>
                line.Contains("Exception:", StringComparison.OrdinalIgnoreCase)
                || line.Contains("not installable", StringComparison.OrdinalIgnoreCase)
                || line.Contains("failed", StringComparison.OrdinalIgnoreCase));

        return line ?? string.Empty;
    }

    private sealed record VsixInstallerResult(
        bool Success,
        int ExitCode,
        string LogText,
        string Message)
    {
        public static VsixInstallerResult Ok(int exitCode, string logText)
            => new(true, exitCode, logText, "VSIXInstaller completed successfully.");

        public static VsixInstallerResult Fail(int exitCode, string logText, string message)
            => new(false, exitCode, logText, message);
    }
}
