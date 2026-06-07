using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class InstalledExtensionScanner(VsixManifestReader manifestReader, UpdateSourceStore sourceStore)
{
    public async Task<IReadOnlyList<InstalledExtension>> ScanAsync(IReadOnlyList<SsmsInstance> instances, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, UpdateSource> sources = await sourceStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        List<InstalledExtension> extensions = [];

        foreach (SsmsInstance instance in instances)
        {
            foreach (string root in instance.ExtensionRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (string manifestPath in Directory.EnumerateFiles(root, "extension.vsixmanifest", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string installPath = Path.GetDirectoryName(manifestPath)!;
                    try
                    {
                        VsixManifest manifest = manifestReader.ReadFromInstalledFolder(installPath);
                        sources.TryGetValue(manifest.Id, out UpdateSource? source);
                        extensions.Add(new InstalledExtension(
                            manifest,
                            instance,
                            installPath,
                            IsPerUser(root),
                            source,
                            null));
                    }
                    catch (Exception)
                    {
                        // Ignore malformed extension folders; the UI should stay focused on manageable VSIX installs.
                    }
                }
            }
        }

        return extensions
            .GroupBy(extension => $"{extension.SsmsInstance.Id}|{extension.Manifest.Id}|{extension.InstallPath}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(extension => extension.Manifest.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsPerUser(string extensionRoot)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return extensionRoot.StartsWith(localAppData, StringComparison.OrdinalIgnoreCase);
    }
}
