using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public static class ExtensionPackageSource
{
    public const string VsixExtension = ".vsix";
    public const string ZipExtension = ".zip";

    public static bool TryGetDirectSourceType(string value, out UpdateSourceType sourceType)
    {
        sourceType = UpdateSourceType.Unknown;
        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)
            && TryGetDirectSourceType(uri, out sourceType);
    }

    public static bool TryGetDirectSourceType(Uri uri, out UpdateSourceType sourceType)
    {
        sourceType = Path.GetExtension(uri.LocalPath) switch
        {
            string extension when extension.Equals(VsixExtension, StringComparison.OrdinalIgnoreCase) => UpdateSourceType.DirectVsixUrl,
            string extension when extension.Equals(ZipExtension, StringComparison.OrdinalIgnoreCase) => UpdateSourceType.DirectZipUrl,
            _ => UpdateSourceType.Unknown
        };

        return sourceType != UpdateSourceType.Unknown;
    }

    public static bool IsSupportedAssetName(string name)
    {
        string extension = Path.GetExtension(name);
        return extension.Equals(VsixExtension, StringComparison.OrdinalIgnoreCase)
            || extension.Equals(ZipExtension, StringComparison.OrdinalIgnoreCase);
    }
}
