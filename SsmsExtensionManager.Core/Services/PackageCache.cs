using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class PackageCache
{
    private readonly string _cacheRoot;

    public PackageCache(string? cacheRoot = null)
    {
        _cacheRoot = cacheRoot ?? AppPaths.PackageCacheRoot;
    }

    public string CacheVsix(string vsixPath, VsixManifest manifest)
    {
        if (!File.Exists(vsixPath))
        {
            throw new FileNotFoundException("VSIX file was not found.", vsixPath);
        }

        string extensionRoot = Path.Combine(_cacheRoot, SafePathPart(manifest.Id), SafePathPart(manifest.Version));
        Directory.CreateDirectory(extensionRoot);

        string targetPath = Path.Combine(extensionRoot, $"{SafePathPart(manifest.Id)}.vsix");
        File.Copy(vsixPath, targetPath, overwrite: true);
        return targetPath;
    }

    public void RemoveCachedPackage(string? cachedVsixPath)
    {
        if (string.IsNullOrWhiteSpace(cachedVsixPath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(cachedVsixPath);
        string fullRoot = Path.GetFullPath(_cacheRoot);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        string? versionDirectory = Path.GetDirectoryName(fullPath);
        TryDeleteEmptyDirectory(versionDirectory);
        TryDeleteEmptyDirectory(Path.GetDirectoryName(versionDirectory ?? ""));
    }

    private static void TryDeleteEmptyDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }
    }

    private static string SafePathPart(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
    }
}
