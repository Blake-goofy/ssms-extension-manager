namespace SsmsExtensionManager.Core.Services;

public static class ExternalUriPolicy
{
    private static readonly HashSet<string> ApprovedBrowserHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        GalleryConstants.Host,
        "github.com"
    };

    public static bool IsApprovedBrowserUri(Uri? uri)
        => uri is not null
            && uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && ApprovedBrowserHosts.Contains(uri.Host);

    public static bool IsHttpsUri(Uri? uri)
        => uri is not null
            && uri.IsAbsoluteUri
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
}
