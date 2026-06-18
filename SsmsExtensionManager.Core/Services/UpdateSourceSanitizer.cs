using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

internal static class UpdateSourceSanitizer
{
    public static UpdateSource? Normalize(UpdateSource? source)
    {
        if (source is null || string.IsNullOrWhiteSpace(source.Uri))
        {
            return source;
        }

        string normalizedUri = source.Uri.Trim();
        if (ShouldStripQuery(source.Type)
            && Uri.TryCreate(normalizedUri, UriKind.Absolute, out Uri? uri)
            && ExternalUriPolicy.IsHttpsUri(uri))
        {
            normalizedUri = uri.GetLeftPart(UriPartial.Path);
        }

        return source with { Uri = normalizedUri };
    }

    private static bool ShouldStripQuery(UpdateSourceType type)
        => type is UpdateSourceType.GitHubRelease or UpdateSourceType.DirectVsixUrl or UpdateSourceType.DirectZipUrl;
}
