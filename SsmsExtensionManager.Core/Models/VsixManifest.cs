namespace SsmsExtensionManager.Core.Models;

public sealed record VsixManifest(
    string Id,
    string Version,
    string Publisher,
    string DisplayName,
    string? Description,
    string? MoreInfo,
    string? ReleaseNotes);
