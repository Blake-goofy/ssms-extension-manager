using System.IO.Compression;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class ExtensionAssetResolver(VsixManifestReader manifestReader)
{
    private const int MaxZipEntryCount = 256;
    private const long MaxExpandedZipBytes = 512L * 1024L * 1024L;
    private const long MaxVsixEntryBytes = 256L * 1024L * 1024L;
    private const double MaxCompressionRatio = 100.0;

    public ExtensionAsset Resolve(string assetPath, string extractionRoot)
    {
        string extension = Path.GetExtension(assetPath);

        if (extension.Equals(ExtensionPackageSource.VsixExtension, StringComparison.OrdinalIgnoreCase))
        {
            return new ExtensionAsset(assetPath, manifestReader.ReadFromVsix(assetPath), Path.GetFileName(assetPath));
        }

        if (extension.Equals(ExtensionPackageSource.ZipExtension, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(extractionRoot);
            string extractPath = Path.Combine(extractionRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractPath);

            string vsixPath = ExtractSingleVsix(assetPath, extractPath);
            return new ExtensionAsset(vsixPath, manifestReader.ReadFromVsix(vsixPath), Path.GetFileName(assetPath));
        }

        throw new NotSupportedException("Only .vsix and .zip assets are supported.");
    }

    private static string ExtractSingleVsix(string assetPath, string extractPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(assetPath);
        if (archive.Entries.Count > MaxZipEntryCount)
        {
            throw new InvalidDataException("ZIP file contains too many entries.");
        }

        long expandedBytes = 0;
        ZipArchiveEntry? vsixEntry = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (entry.Length > MaxExpandedZipBytes - expandedBytes)
            {
                throw new InvalidDataException("ZIP file is too large after extraction.");
            }

            expandedBytes += entry.Length;

            if (entry.Length > 0 && entry.CompressedLength <= 0)
            {
                throw new InvalidDataException("ZIP file has invalid compressed entry metadata.");
            }

            if (entry.Length > 0 && (double)entry.Length / entry.CompressedLength > MaxCompressionRatio)
            {
                throw new InvalidDataException("ZIP file compression ratio is too high.");
            }

            if (!Path.GetExtension(entry.FullName).Equals(ExtensionPackageSource.VsixExtension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Length > MaxVsixEntryBytes)
            {
                throw new InvalidDataException("VSIX entry is too large.");
            }

            if (vsixEntry is not null)
            {
                throw new InvalidDataException("ZIP file contains multiple VSIX files. Multi-select support is not implemented yet.");
            }

            vsixEntry = entry;
        }

        if (vsixEntry is null)
        {
            throw new InvalidDataException("ZIP file does not contain a VSIX.");
        }

        string vsixPath = Path.Combine(extractPath, Path.GetFileName(vsixEntry.FullName));
        vsixEntry.ExtractToFile(vsixPath);
        return vsixPath;
    }
}
