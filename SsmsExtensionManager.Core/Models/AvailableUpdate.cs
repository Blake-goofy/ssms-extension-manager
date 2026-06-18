namespace SsmsExtensionManager.Core.Models;

public sealed record AvailableUpdate(
    string Version,
    Uri AssetUri,
    string ReleaseName,
    DateTimeOffset PublishedAt,
    string? Sha256 = null);
