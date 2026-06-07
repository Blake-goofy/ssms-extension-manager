namespace SsmsExtensionManager.Core.Models;

public sealed record ExtensionAsset(
    string FilePath,
    VsixManifest Manifest,
    string SourceDescription);
