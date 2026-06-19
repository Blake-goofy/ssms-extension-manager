using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public static class GalleryExtensionMatcher
{
    public static bool IsMatch(VsixManifest manifest, GalleryExtension galleryExtension)
    {
        return string.Equals(manifest.Id, galleryExtension.Id, StringComparison.OrdinalIgnoreCase)
            && IsCompatible(manifest, galleryExtension);
    }

    public static GalleryExtension? MatchForManifest(VsixManifest manifest, IReadOnlyDictionary<string, GalleryExtension> galleryById)
    {
        if (!galleryById.TryGetValue(manifest.Id, out GalleryExtension? galleryExtension))
        {
            return null;
        }

        return IsMatch(manifest, galleryExtension)
            ? galleryExtension
            : null;
    }

    public static bool IsCompatible(VsixManifest manifest, GalleryExtension galleryExtension)
    {
        if (string.IsNullOrWhiteSpace(manifest.DisplayName)
            || string.IsNullOrWhiteSpace(galleryExtension.DisplayName)
            || string.IsNullOrWhiteSpace(manifest.Publisher)
            || string.IsNullOrWhiteSpace(galleryExtension.Author))
        {
            return false;
        }

        return string.Equals(manifest.DisplayName.Trim(), galleryExtension.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase)
            && ValueNormalization.PublisherKey(manifest.Publisher) == ValueNormalization.PublisherKey(galleryExtension.Author);
    }

    public static UpdateSource? KeepCompatibleSource(UpdateSource? source, GalleryExtension? galleryExtension)
    {
        if (source is null)
        {
            return null;
        }

        if (!IsGallerySource(source))
        {
            return source;
        }

        return galleryExtension is not null && IsGalleryPackageSource(source, galleryExtension)
            ? source
            : null;
    }

    public static bool IsGallerySource(UpdateSource? source)
    {
        return Uri.TryCreate(source?.Uri, UriKind.Absolute, out Uri? uri)
            && string.Equals(uri.Host, GalleryConstants.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGalleryPackageSource(UpdateSource source, GalleryExtension galleryExtension)
        => Uri.TryCreate(source.Uri, UriKind.Absolute, out Uri? sourceUri)
            && Uri.Compare(
                sourceUri,
                galleryExtension.PackageUri,
                UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.Unescaped,
                StringComparison.OrdinalIgnoreCase) == 0;

}
