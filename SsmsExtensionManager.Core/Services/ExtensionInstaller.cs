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

        OperationResult installerResult = RunVsixInstaller(instance, BuildInstallArguments(asset.FilePath), cancellationToken);
        return installerResult.Success
            ? OperationResult.Ok($"Install complete for {asset.Manifest.DisplayName} {asset.Manifest.Version}.")
            : installerResult;
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

        if (!string.Equals(asset.Manifest.Id, installedExtension.Manifest.Id, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail($"Downloaded VSIX identity '{asset.Manifest.Id}' does not match installed extension '{installedExtension.Manifest.Id}'.");
        }

        OperationResult installerResult = RunVsixInstaller(installedExtension.SsmsInstance, BuildInstallArguments(asset.FilePath), cancellationToken);
        return installerResult.Success
            ? OperationResult.Ok($"Update complete for {asset.Manifest.DisplayName} {asset.Manifest.Version}.")
            : installerResult;
    }

    public OperationResult Uninstall(InstalledExtension extension, CancellationToken cancellationToken = default)
    {
        OperationResult installerResult = RunVsixInstaller(extension.SsmsInstance, BuildUninstallArguments(extension.Manifest.Id), cancellationToken);
        return installerResult.Success
            ? OperationResult.Ok($"Uninstall complete for {extension.Manifest.DisplayName}.")
            : installerResult;
    }

    private static OperationResult RunVsixInstaller(SsmsInstance instance, string arguments, CancellationToken cancellationToken)
    {
        string installerPath = SsmsPaths.GetVsixInstallerPath(instance.InstallationPath);
        if (!File.Exists(installerPath))
        {
            return OperationResult.Fail($"{SsmsPaths.VsixInstallerFileName} was not found at {installerPath}.");
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

            return OperationResult.Fail(ex.Message);
        }

        if (process.ExitCode != 0)
        {
            return OperationResult.Fail($"VSIXInstaller exited with code {process.ExitCode}.");
        }

        return OperationResult.Ok("VSIXInstaller completed successfully.");
    }

    private static string BuildInstallArguments(string assetPath) => $"/quiet {QuoteArgument(assetPath)}";

    private static string BuildUninstallArguments(string extensionId) => $"/quiet /u:{extensionId}";

    private static string QuoteArgument(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}
