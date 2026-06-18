using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public static class VsixUpdateIdentityPolicy
{
    public static bool TryValidateUpdate(VsixManifest expected, VsixManifest candidate, out string errorMessage)
    {
        if (!string.Equals(candidate.Id, expected.Id, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Downloaded VSIX identity '{candidate.Id}' does not match expected extension '{expected.Id}'.";
            return false;
        }

        if (PublisherKey(candidate.Publisher) != PublisherKey(expected.Publisher))
        {
            errorMessage = $"Downloaded VSIX publisher '{candidate.Publisher}' does not match expected publisher '{expected.Publisher}'.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static bool IsTrustedUpdate(VsixManifest expected, VsixManifest candidate)
        => TryValidateUpdate(expected, candidate, out _);

    private static string PublisherKey(string publisher)
    {
        string key = publisher.Trim().ToUpperInvariant();
        foreach (string token in new[] { " CORPORATION", " CORP.", " CORP" })
        {
            if (key.EndsWith(token, StringComparison.Ordinal))
            {
                key = key[..^token.Length].TrimEnd();
                break;
            }
        }

        return key;
    }
}
