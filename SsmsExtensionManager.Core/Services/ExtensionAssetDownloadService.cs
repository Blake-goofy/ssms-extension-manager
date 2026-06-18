using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class ExtensionAssetDownloadService(HttpClient httpClient, ExtensionAssetResolver assetResolver)
{
    public async Task<DownloadedExtensionAsset> DownloadAndResolveAsync(
        Uri uri,
        string extractionRoot,
        CancellationToken cancellationToken = default)
    {
        DownloadedPackage downloaded = await DownloadAsync(uri, cancellationToken).ConfigureAwait(false);
        try
        {
            ExtensionAsset asset = assetResolver.Resolve(downloaded.Path, extractionRoot);
            return new DownloadedExtensionAsset(asset, downloaded.Path, downloaded.LastModified);
        }
        catch
        {
            downloaded.Dispose();
            throw;
        }
    }

    private async Task<DownloadedPackage> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        string targetRoot = AppPaths.TempDownloadsRoot;
        Directory.CreateDirectory(targetRoot);
        string targetPath = Path.Combine(targetRoot, $"{Guid.NewGuid():N}{Path.GetExtension(uri.LocalPath)}");

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream output = File.Create(targetPath);
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            return new DownloadedPackage(targetPath, response.Content.Headers.LastModified);
        }
        catch
        {
            DownloadedFileCleanup.TryDelete(targetPath);
            throw;
        }
    }

    private sealed class DownloadedPackage(string path, DateTimeOffset? lastModified) : IDisposable
    {
        private bool _disposed;

        public string Path { get; } = path;

        public DateTimeOffset? LastModified { get; } = lastModified;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DownloadedFileCleanup.TryDelete(Path);
        }
    }
}

public sealed class DownloadedExtensionAsset(ExtensionAsset asset, string downloadPath, DateTimeOffset? lastModified) : IDisposable
{
    private bool _disposed;

    public ExtensionAsset Asset { get; } = asset;

    public string DownloadPath { get; } = downloadPath;

    public DateTimeOffset? LastModified { get; } = lastModified;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DownloadedFileCleanup.TryDelete(DownloadPath);
    }
}

internal static class DownloadedFileCleanup
{
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }
}
