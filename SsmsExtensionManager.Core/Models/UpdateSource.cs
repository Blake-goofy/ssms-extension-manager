namespace SsmsExtensionManager.Core.Models;

public enum UpdateSourceType
{
    Unknown,
    GitHubRelease,
    DirectVsixUrl,
    DirectZipUrl,
    Manual
}

public sealed record UpdateSource(
    UpdateSourceType Type,
    string Uri);
