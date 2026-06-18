using System.Security.Cryptography;
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
            return new DownloadedExtensionAsset(asset, downloaded.Path, downloaded.LastModified, downloaded.Sha256);
        }
        catch
        {
            downloaded.Dispose();
            throw;
        }
    }

    private async Task<DownloadedPackage> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (!ExternalUriPolicy.IsHttpsUri(uri))
        {
            throw new InvalidOperationException("Extension packages must be downloaded over HTTPS.");
        }

        string targetRoot = AppPaths.TempDownloadsRoot;
        Directory.CreateDirectory(targetRoot);
        string targetPath = Path.Combine(targetRoot, $"{Guid.NewGuid():N}{Path.GetExtension(uri.LocalPath)}");

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (FileStream output = File.Create(targetPath))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            string sha256 = await ComputeSha256Async(targetPath, cancellationToken).ConfigureAwait(false);
            return new DownloadedPackage(targetPath, response.Content.Headers.LastModified, sha256);
        }
        catch
        {
            DownloadedFileCleanup.TryDelete(targetPath);
            throw;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class DownloadedPackage(string path, DateTimeOffset? lastModified, string sha256) : IDisposable
    {
        private bool _disposed;

        public string Path { get; } = path;

        public DateTimeOffset? LastModified { get; } = lastModified;

        public string Sha256 { get; } = sha256;

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

public sealed class DownloadedExtensionAsset(ExtensionAsset asset, string downloadPath, DateTimeOffset? lastModified, string sha256) : IDisposable
{
    private bool _disposed;

    public ExtensionAsset Asset { get; } = asset;

    public string DownloadPath { get; } = downloadPath;

    public DateTimeOffset? LastModified { get; } = lastModified;

    public string Sha256 { get; } = sha256;

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
