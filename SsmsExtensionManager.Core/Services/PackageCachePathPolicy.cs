namespace SsmsExtensionManager.Core.Services;

public static class PackageCachePathPolicy
{
    public static bool TryNormalizeCachedVsixPath(string? cachedVsixPath, string cacheRoot, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(cachedVsixPath)
            || !Path.GetExtension(cachedVsixPath).Equals(ExtensionPackageSource.VsixExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(cachedVsixPath);
            string fullRoot = Path.GetFullPath(cacheRoot);
            string relativePath = Path.GetRelativePath(fullRoot, fullPath);
            if (relativePath == "."
                || relativePath.StartsWith("..", StringComparison.Ordinal)
                || Path.IsPathRooted(relativePath))
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
