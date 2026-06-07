namespace SsmsExtensionManager.Core.Models;

public sealed record SsmsInstance(
    string Id,
    string DisplayName,
    string? Version,
    string InstallationPath,
    IReadOnlyList<string> ExtensionRoots);
