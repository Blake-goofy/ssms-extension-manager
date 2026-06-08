namespace SsmsExtensionManager.Core.Models;

public sealed record GalleryExtension(
    string Id,
    string DisplayName,
    string? Summary,
    string? Author,
    string Version,
    Uri? PageUri,
    Uri PackageUri,
    Uri? IconUri,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? UpdatedAt);
