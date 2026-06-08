namespace SsmsExtensionManager.Core.Models;

public sealed record ManagedExtensionRecord(
    string SsmsInstanceId,
    VsixManifest Manifest,
    UpdateSource? UpdateSource,
    string? CachedVsixPath,
    bool IsInstalled,
    DateTimeOffset LastSeenAt,
    string? InstalledVersionOverride = null);
