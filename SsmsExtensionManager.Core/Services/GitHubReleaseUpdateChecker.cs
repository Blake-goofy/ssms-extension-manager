using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class GitHubReleaseUpdateChecker(HttpClient httpClient, ExtensionAssetResolver assetResolver)
{
    public async Task<AvailableUpdate?> CheckAsync(InstalledExtension extension, CancellationToken cancellationToken = default)
    {
        if (extension.UpdateSource is not { } source)
        {
            return null;
        }

        AvailableUpdate? latest = await FindLatestMatchingAssetAsync(extension.Manifest, source, cancellationToken).ConfigureAwait(false);
        return latest is not null && VersionComparer.IsNewer(latest.Version, extension.Manifest.Version)
            ? latest
            : null;
    }

    public async Task<AvailableUpdate?> FindLatestMatchingAssetAsync(VsixManifest manifest, UpdateSource source, CancellationToken cancellationToken = default)
    {
        if (source.Type != UpdateSourceType.GitHubRelease)
        {
            return null;
        }

        if (!GitHubRepository.TryParse(source.Uri, out GitHubRepository repository))
        {
            return null;
        }

        using HttpRequestMessage request = new(HttpMethod.Get, repository.ApiLatestReleaseUri);
        request.Headers.UserAgent.ParseAdd("SsmsExtensionManager/0.1");

        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        GitHubRelease? release = await response.Content.ReadFromJsonAsync<GitHubRelease>(JsonOptions.Default, cancellationToken).ConfigureAwait(false);
        if (release is null)
        {
            return null;
        }

        foreach (GitHubAsset asset in release.Assets.Where(asset => IsSupportedAsset(asset.Name)))
        {
            string downloaded = await DownloadAsync(asset.BrowserDownloadUrl, cancellationToken).ConfigureAwait(false);
            try
            {
                ExtensionAsset resolved = assetResolver.Resolve(downloaded, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "updates"));
                if (!string.Equals(resolved.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new AvailableUpdate(
                    resolved.Manifest.Version,
                    asset.BrowserDownloadUrl,
                    string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                    release.PublishedAt);
            }
            finally
            {
                TryDelete(downloaded);
            }
        }

        return null;
    }

    private async Task<string> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "downloads"));
        string targetPath = Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "downloads", $"{Guid.NewGuid():N}{Path.GetExtension(uri.LocalPath)}");

        await using Stream input = await httpClient.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
        await using FileStream output = File.Create(targetPath);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return targetPath;
    }

    private static bool IsSupportedAsset(string name)
    {
        return name.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
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

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        string? Name,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
        IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        string Name,
        [property: JsonPropertyName("browser_download_url")] Uri BrowserDownloadUrl);
}
