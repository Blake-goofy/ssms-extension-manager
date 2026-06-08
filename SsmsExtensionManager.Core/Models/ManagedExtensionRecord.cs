namespace SsmsExtensionManager.Core.Models;

public sealed record ManagedExtensionRecord(
    string SsmsInstanceId,
    VsixManifest Manifest,
    UpdateSource? UpdateSource,
    string? CachedVsixPath,
    bool IsInstalled,
    DateTimeOffset LastSeenAt,
    string? InstalledVersionOverride = null,
    string? TimestampKind = null,
    DateTimeOffset? TimestampAt = null)
{
    public const string DetectedTimestampKind = "Detected";
    public const string InstalledTimestampKind = "Installed";
    public const string UpdatedTimestampKind = "Updated";
    public const string UninstalledTimestampKind = "Uninstalled";
}
