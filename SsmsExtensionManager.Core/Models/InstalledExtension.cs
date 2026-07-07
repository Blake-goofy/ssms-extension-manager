namespace SsmsExtensionManager.Core.Models;

public sealed record InstalledExtension(
    VsixManifest Manifest,
    SsmsInstance SsmsInstance,
    string InstallPath,
    bool IsPerUser,
    UpdateSource? UpdateSource,
    AvailableUpdate? AvailableUpdate,
    string? InstalledVersionOverride = null)
{
    public string CurrentVersion => Manifest.Version;
}
