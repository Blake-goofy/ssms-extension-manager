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

        if (ValueNormalization.PublisherKey(candidate.Publisher) != ValueNormalization.PublisherKey(expected.Publisher))
        {
            errorMessage = $"Downloaded VSIX publisher '{candidate.Publisher}' does not match expected publisher '{expected.Publisher}'.";
            return false;
        }

        errorMessage = string.Empty;
        return true;
    }

    public static bool IsTrustedUpdate(VsixManifest expected, VsixManifest candidate)
        => TryValidateUpdate(expected, candidate, out _);

}
