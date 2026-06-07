using System.Diagnostics;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class ExtensionInstaller(ExtensionAssetResolver assetResolver)
{
    public OperationResult InstallLocalAsset(SsmsInstance instance, string assetPath)
    {
        ExtensionAsset asset;
        try
        {
            asset = assetResolver.Resolve(assetPath, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        OperationResult installerResult = RunVsixInstaller(instance, BuildInstallArguments(asset.FilePath));
        return installerResult.Success
            ? OperationResult.Ok($"Install complete for {asset.Manifest.DisplayName} {asset.Manifest.Version}.")
            : installerResult;
    }

    public OperationResult UpdateInstalledExtension(InstalledExtension installedExtension, string assetPath)
    {
        ExtensionAsset asset;
        try
        {
            asset = assetResolver.Resolve(assetPath, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }

        if (!string.Equals(asset.Manifest.Id, installedExtension.Manifest.Id, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail($"Downloaded VSIX identity '{asset.Manifest.Id}' does not match installed extension '{installedExtension.Manifest.Id}'.");
        }

        OperationResult installerResult = RunVsixInstaller(installedExtension.SsmsInstance, BuildInstallArguments(asset.FilePath));
        return installerResult.Success
            ? OperationResult.Ok($"Update complete for {asset.Manifest.DisplayName} {asset.Manifest.Version}.")
            : installerResult;
    }

    public OperationResult Uninstall(InstalledExtension extension)
    {
        OperationResult installerResult = RunVsixInstaller(extension.SsmsInstance, BuildUninstallArguments(extension.Manifest.Id));
        return installerResult.Success
            ? OperationResult.Ok($"Uninstall complete for {extension.Manifest.DisplayName}.")
            : installerResult;
    }

    private static OperationResult RunVsixInstaller(SsmsInstance instance, string arguments)
    {
        string installerPath = Path.Combine(instance.InstallationPath, "Common7", "IDE", "VSIXInstaller.exe");
        if (!File.Exists(installerPath))
        {
            return OperationResult.Fail($"VSIXInstaller.exe was not found at {installerPath}.");
        }

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            process.Start();
            process.WaitForExit();
        }
        catch (Exception ex)
        {
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
