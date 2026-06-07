using System.IO.Compression;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class ExtensionAssetResolver(VsixManifestReader manifestReader)
{
    public ExtensionAsset Resolve(string assetPath, string extractionRoot)
    {
        string extension = Path.GetExtension(assetPath);

        if (extension.Equals(".vsix", StringComparison.OrdinalIgnoreCase))
        {
            return new ExtensionAsset(assetPath, manifestReader.ReadFromVsix(assetPath), Path.GetFileName(assetPath));
        }

        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(extractionRoot);
            string extractPath = Path.Combine(extractionRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractPath);
            ZipFile.ExtractToDirectory(assetPath, extractPath);

            string[] vsixFiles = Directory.GetFiles(extractPath, "*.vsix", SearchOption.AllDirectories);
            if (vsixFiles.Length == 0)
            {
                throw new InvalidDataException("ZIP file does not contain a VSIX.");
            }

            if (vsixFiles.Length > 1)
            {
                throw new InvalidDataException("ZIP file contains multiple VSIX files. Multi-select support is not implemented yet.");
            }

            string vsixPath = vsixFiles[0];
            return new ExtensionAsset(vsixPath, manifestReader.ReadFromVsix(vsixPath), Path.GetFileName(assetPath));
        }

        throw new NotSupportedException("Only .vsix and .zip assets are supported.");
    }
}
