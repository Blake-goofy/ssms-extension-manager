using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class GitHubReleaseUpdateChecker(HttpClient httpClient, ExtensionAssetDownloadService assetDownloadService)
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
        return source.Type switch
        {
            UpdateSourceType.GitHubRelease => await FindGitHubReleaseAssetAsync(manifest, source, cancellationToken).ConfigureAwait(false),
            UpdateSourceType.DirectVsixUrl or UpdateSourceType.DirectZipUrl => await FindDirectAssetAsync(manifest, source, cancellationToken).ConfigureAwait(false),
            _ => null
        };
    }

    private async Task<AvailableUpdate?> FindGitHubReleaseAssetAsync(VsixManifest manifest, UpdateSource source, CancellationToken cancellationToken)
    {
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
            using DownloadedExtensionAsset downloaded = await assetDownloadService
                .DownloadAndResolveAsync(asset.BrowserDownloadUrl, AppPaths.TempUpdatesRoot, cancellationToken)
                .ConfigureAwait(false);
            ExtensionAsset resolved = downloaded.Asset;
            if (!string.Equals(resolved.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string resolvedVersion = VersionComparer.ExtractVersionText(release.TagName)
                ?? VersionComparer.ExtractVersionText(release.Name ?? string.Empty)
                ?? resolved.Manifest.Version;

            return new AvailableUpdate(
                resolvedVersion,
                asset.BrowserDownloadUrl,
                string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name,
                release.PublishedAt);
        }

        return null;
    }

    private async Task<AvailableUpdate?> FindDirectAssetAsync(VsixManifest manifest, UpdateSource source, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(source.Uri, UriKind.Absolute, out Uri? assetUri))
        {
            return null;
        }

        using DownloadedExtensionAsset downloaded = await assetDownloadService
            .DownloadAndResolveAsync(assetUri, AppPaths.TempUpdatesRoot, cancellationToken)
            .ConfigureAwait(false);
        ExtensionAsset resolved = downloaded.Asset;
        if (!string.Equals(resolved.Manifest.Id, manifest.Id, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string releaseName = Path.GetFileName(assetUri.LocalPath);
        if (string.IsNullOrWhiteSpace(releaseName))
        {
            releaseName = resolved.SourceDescription;
        }

        return new AvailableUpdate(
            resolved.Manifest.Version,
            assetUri,
            releaseName,
            downloaded.LastModified ?? DateTimeOffset.UtcNow);
    }

    private static bool IsSupportedAsset(string name)
        => ExtensionPackageSource.IsSupportedAssetName(name);

    private sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string TagName,
        string? Name,
        [property: JsonPropertyName("published_at")] DateTimeOffset PublishedAt,
        IReadOnlyList<GitHubAsset> Assets);

    private sealed record GitHubAsset(
        string Name,
        [property: JsonPropertyName("browser_download_url")] Uri BrowserDownloadUrl);

}
