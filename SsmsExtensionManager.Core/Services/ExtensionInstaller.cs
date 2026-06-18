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

        VsixInstallerResult installerResult;
        try
        {
            installerResult = InstallWithInteractiveFallback(instance, asset, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }

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

        VsixInstallerResult installerResult;
        try
        {
            installerResult = InstallWithInteractiveFallback(installedExtension.SsmsInstance, asset, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }

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

    private static VsixInstallerResult InstallWithInteractiveFallback(SsmsInstance instance, ExtensionAsset asset, CancellationToken cancellationToken)
    {
        using StagedExtensionAsset stagedAsset = StageForVsixInstaller(asset, AppPaths.InstallStagingRoot);

        VsixInstallerResult installerResult = RunVsixInstaller(instance, BuildInstallArguments(stagedAsset.Asset.FilePath), cancellationToken);
        if (installerResult.Success || !IsAlreadyInstalledFailure(installerResult.ExitCode, installerResult.LogText))
        {
            return installerResult;
        }

        return RunVsixInstaller(instance, BuildInteractiveInstallArguments(stagedAsset.Asset.FilePath), cancellationToken);
    }

    private static VsixInstallerResult RunVsixInstaller(SsmsInstance instance, string arguments, CancellationToken cancellationToken)
    {
        string installerPath = SsmsPaths.GetVsixInstallerPath(instance.InstallationPath);
        if (!File.Exists(installerPath))
        {
            return VsixInstallerResult.Fail(-1, string.Empty, $"{SsmsPaths.VsixInstallerFileName} was not found at {installerPath}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        DateTime logSearchStartUtc = DateTime.UtcNow.AddSeconds(-2);

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = arguments
            }
        };

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

    internal static string BuildInteractiveInstallArguments(string assetPath) => QuoteArgument(assetPath);

    private static string BuildUninstallArguments(string extensionId) => $"/quiet /u:{extensionId}";

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    internal static bool IsAlreadyInstalledFailure(int exitCode, string logText)
        => exitCode == 1001
            && (logText.Contains("AlreadyInstalledException", StringComparison.OrdinalIgnoreCase)
                || logText.Contains("already installed to all applicable products", StringComparison.OrdinalIgnoreCase));

    internal static StagedExtensionAsset StageForVsixInstaller(ExtensionAsset asset, string stagingRoot)
    {
        if (!File.Exists(asset.FilePath))
        {
            throw new FileNotFoundException("VSIX file was not found.", asset.FilePath);
        }

        string stagingDirectory = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);

        string fileName = SafeVsixFileName(Path.GetFileName(asset.FilePath), asset.Manifest.Id);
        string stagedPath = Path.Combine(stagingDirectory, fileName);
        File.Copy(asset.FilePath, stagedPath, overwrite: false);

        return new StagedExtensionAsset(
            asset with { FilePath = stagedPath },
            stagingDirectory);
    }

    private static string SafeVsixFileName(string? preferredName, string fallbackName)
    {
        string fileName = string.IsNullOrWhiteSpace(preferredName)
            ? $"{fallbackName}{ExtensionPackageSource.VsixExtension}"
            : preferredName;

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '-');
        }

        if (!Path.GetExtension(fileName).Equals(ExtensionPackageSource.VsixExtension, StringComparison.OrdinalIgnoreCase))
        {
            fileName += ExtensionPackageSource.VsixExtension;
        }

        return fileName;
    }

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

    internal sealed class StagedExtensionAsset(ExtensionAsset asset, string stagingDirectory) : IDisposable
    {
        public ExtensionAsset Asset { get; } = asset;

        public string StagingDirectory { get; } = stagingDirectory;

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(StagingDirectory))
                {
                    Directory.Delete(StagingDirectory, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; a running installer or scanner can briefly hold the file.
            }
        }
    }
}
