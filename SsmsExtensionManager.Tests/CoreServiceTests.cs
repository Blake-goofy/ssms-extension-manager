using System.IO.Compression;
using System.Net;
using System.Net.Http;
using SsmsExtensionManager.Core.Models;
using SsmsExtensionManager.Core.Services;

namespace SsmsExtensionManager.Tests;

public sealed class CoreServiceTests
{
    [Theory]
    [InlineData("https://github.com/Axial-SQL/AxialSqlTools", "Axial-SQL", "AxialSqlTools")]
    [InlineData("https://github.com/Axial-SQL/AxialSqlTools/releases/latest", "Axial-SQL", "AxialSqlTools")]
    [InlineData("https://github.com/Axial-SQL/AxialSqlTools.git", "Axial-SQL", "AxialSqlTools")]
    [InlineData("Axial-SQL/AxialSqlTools", "Axial-SQL", "AxialSqlTools")]
    [InlineData("http://github.com/owner/repo/releases/latest", "owner", "repo")]
    public void GitHubRepository_ParsesExpectedUrls(string url, string owner, string repo)
    {
        Assert.True(GitHubRepository.TryParse(url, out GitHubRepository? repository));
        Assert.Equal(owner, repository.Owner);
        Assert.Equal(repo, repository.Name);
    }

    [Theory]
    [InlineData("1.2.4", "1.2.3", true)]
    [InlineData("v2.0.0", "1.9.9", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    public void VersionComparer_ComparesVsixStyleVersions(string candidate, string installed, bool expected)
    {
        Assert.Equal(expected, VersionComparer.IsNewer(candidate, installed));
    }

    [Theory]
    [InlineData("v2.2.2", "2.2.2")]
    [InlineData("Release 10.4.1", "10.4.1")]
    [InlineData("no version here", null)]
    public void VersionComparer_ExtractVersionText_ReturnsNormalizedVersion(string input, string? expected)
    {
        Assert.Equal(expected, VersionComparer.ExtractVersionText(input));
    }

    [Fact]
    public void VsixManifestReader_ReadsManifestFromVsix()
    {
        string tempRoot = CreateTempRoot();
        string vsixPath = Path.Combine(tempRoot, "sample.vsix");
        CreateVsix(vsixPath, "Sample.Extension", "1.2.3", "Sample Publisher", "Sample Extension");

        VsixManifestReader reader = new();
        var manifest = reader.ReadFromVsix(vsixPath);

        Assert.Equal("Sample.Extension", manifest.Id);
        Assert.Equal("1.2.3", manifest.Version);
        Assert.Equal("Sample Publisher", manifest.Publisher);
        Assert.Equal("Sample Extension", manifest.DisplayName);
    }

    [Fact]
    public void ExtensionAssetResolver_ResolvesZipContainingSingleVsix()
    {
        string tempRoot = CreateTempRoot();
        string vsixPath = Path.Combine(tempRoot, "sample.vsix");
        string zipPath = Path.Combine(tempRoot, "release.zip");
        CreateVsix(vsixPath, "Sample.Extension", "2.0.0", "Sample Publisher", "Sample Extension");

        using (ZipArchive archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            archive.CreateEntryFromFile(vsixPath, "sample.vsix");
        }

        ExtensionAssetResolver resolver = new(new VsixManifestReader());
        var asset = resolver.Resolve(zipPath, Path.Combine(tempRoot, "extract"));

        Assert.Equal("Sample.Extension", asset.Manifest.Id);
        Assert.Equal("2.0.0", asset.Manifest.Version);
        Assert.True(File.Exists(asset.FilePath));
    }

    [Fact]
    public void PackageCache_CopiesAndRemovesVsix()
    {
        string tempRoot = CreateTempRoot();
        string vsixPath = Path.Combine(tempRoot, "sample.vsix");
        CreateVsix(vsixPath, "Sample.Extension", "3.0.0", "Sample Publisher", "Sample Extension");
        var manifest = new VsixManifest("Sample.Extension", "3.0.0", "Sample Publisher", "Sample Extension", null, null, null);

        PackageCache cache = new(Path.Combine(tempRoot, "cache"));
        string cachedPath = cache.CacheVsix(vsixPath, manifest);

        Assert.True(File.Exists(cachedPath));
        Assert.NotEqual(vsixPath, cachedPath);

        cache.RemoveCachedPackage(cachedPath);

        Assert.False(File.Exists(cachedPath));
    }

    [Fact]
    public void GalleryFeedReader_ReadsAtomEntries()
    {
        using MemoryStream stream = new("""
            <?xml version="1.0" encoding="utf-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <id>SSMS_EnvTabs</id>
                <title type="text">SSMS EnvTabs</title>
                <link rel="alternate" href="https://ssmsgallery.azurewebsites.net/extension/SSMS_EnvTabs" />
                <summary type="text">Auto-color and rename SSMS query tabs.</summary>
                <published>2026-05-22T13:17:58Z</published>
                <updated>2026-05-22T13:17:58Z</updated>
                <author>
                  <name>Blake Becker</name>
                </author>
                <content type="application/octet-stream" src="https://ssmsgallery.azurewebsites.net/extensions/SSMS_EnvTabs/extension.vsix" />
                <link rel="icon" href="https://ssmsgallery.azurewebsites.net/extensions/SSMS_EnvTabs/icon-2.2.0.webp" />
                <Vsix xmlns="http://schemas.microsoft.com/developer/vsx-syndication-schema/2010">
                  <Id>SSMS_EnvTabs</Id>
                  <Version>2.2.0</Version>
                </Vsix>
              </entry>
            </feed>
            """u8.ToArray());

        GalleryFeedReader reader = new();
        IReadOnlyList<GalleryExtension> extensions = reader.Read(stream);

        GalleryExtension extension = Assert.Single(extensions);
        Assert.Equal("SSMS_EnvTabs", extension.Id);
        Assert.Equal("SSMS EnvTabs", extension.DisplayName);
        Assert.Equal("Auto-color and rename SSMS query tabs.", extension.Summary);
        Assert.Equal("Blake Becker", extension.Author);
        Assert.Equal("2.2.0", extension.Version);
        Assert.Equal("https://ssmsgallery.azurewebsites.net/extensions/SSMS_EnvTabs/extension.vsix", extension.PackageUri.ToString());
        Assert.Equal("https://ssmsgallery.azurewebsites.net/extensions/SSMS_EnvTabs/icon-2.2.0.webp", extension.IconUri?.ToString());
    }

    [Fact]
    public void GalleryExtensionMatcher_DoesNotMatchSameIdAndNameWithDifferentPublisher()
    {
        var manifest = new VsixManifest("SqlFormatter.Shared", "1.0.0", "Microsoft", "SQL Formatter", null, null, null);
        GalleryExtension galleryExtension = CreateGalleryExtension("SqlFormatter.Shared", "SQL Formatter", "Mads Kristensen");

        GalleryExtension? match = GalleryExtensionMatcher.MatchForManifest(manifest, GalleryById(galleryExtension));

        Assert.Null(match);
    }

    [Fact]
    public void GalleryExtensionMatcher_DoesNotMatchSameIdAndPublisherWithDifferentName()
    {
        var manifest = new VsixManifest("Shared.Extension", "1.0.0", "Vendor A", "Extension One", null, null, null);
        GalleryExtension galleryExtension = CreateGalleryExtension("Shared.Extension", "Extension Two", "Vendor A");

        GalleryExtension? match = GalleryExtensionMatcher.MatchForManifest(manifest, GalleryById(galleryExtension));

        Assert.Null(match);
    }

    [Fact]
    public void GalleryExtensionMatcher_MatchesSameIdNameAndPublisher()
    {
        var manifest = new VsixManifest("Mads.SqlFormatter", "1.0.0", "Mads Kristensen", "SQL Formatter", null, null, null);
        GalleryExtension galleryExtension = CreateGalleryExtension("Mads.SqlFormatter", "SQL Formatter", "Mads Kristensen");

        GalleryExtension? match = GalleryExtensionMatcher.MatchForManifest(manifest, GalleryById(galleryExtension));

        Assert.Same(galleryExtension, match);
    }

    [Fact]
    public void GalleryExtensionMatcher_RemovesStaleGallerySourceWithoutMatch()
    {
        UpdateSource source = new(UpdateSourceType.DirectVsixUrl, "https://ssmsgallery.azurewebsites.net/extensions/SqlFormatter/extension.vsix");

        UpdateSource? kept = GalleryExtensionMatcher.KeepCompatibleSource(source, galleryExtension: null);

        Assert.Null(kept);
    }

    [Fact]
    public void GalleryExtensionMatcher_KeepsCompatibleGallerySource()
    {
        GalleryExtension galleryExtension = CreateGalleryExtension("Mads.SqlFormatter", "SQL Formatter", "Mads Kristensen");
        UpdateSource source = new(UpdateSourceType.DirectVsixUrl, galleryExtension.PackageUri.ToString());

        UpdateSource? kept = GalleryExtensionMatcher.KeepCompatibleSource(source, galleryExtension);

        Assert.Same(source, kept);
    }

    [Fact]
    public void GalleryExtensionMatcher_KeepsNonGallerySourceWithoutMatch()
    {
        UpdateSource source = new(UpdateSourceType.GitHubRelease, "https://github.com/owner/repo");

        UpdateSource? kept = GalleryExtensionMatcher.KeepCompatibleSource(source, galleryExtension: null);

        Assert.Same(source, kept);
    }

    [Fact]
    public async Task GitHubReleaseUpdateChecker_ReadsLatestVersionFromDirectVsixUrl()
    {
        string tempRoot = CreateTempRoot();
        string vsixPath = Path.Combine(tempRoot, "sample.vsix");
        CreateVsix(vsixPath, "Sample.Extension", "4.2.0", "Sample Publisher", "Sample Extension");
        byte[] bytes = await File.ReadAllBytesAsync(vsixPath);

        HttpClient httpClient = new(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
            {
                Headers =
                {
                    LastModified = DateTimeOffset.UtcNow
                }
            }
        }));

        GitHubReleaseUpdateChecker checker = new(httpClient, new ExtensionAssetResolver(new VsixManifestReader()));
        var manifest = new VsixManifest("Sample.Extension", "1.0.0", "Sample Publisher", "Sample Extension", null, null, null);
        UpdateSource source = new(UpdateSourceType.DirectVsixUrl, "https://example.com/extensions/sample.vsix");

        AvailableUpdate? update = await checker.FindLatestMatchingAssetAsync(manifest, source);

        Assert.NotNull(update);
        Assert.Equal("4.2.0", update!.Version);
        Assert.Equal("https://example.com/extensions/sample.vsix", update.AssetUri.ToString());
    }

    [Fact]
    public async Task ManagedExtensionStore_UpsertsAndRemovesRecords()
    {
        string tempRoot = CreateTempRoot();
        ManagedExtensionStore store = new(Path.Combine(tempRoot, "managed-extensions.json"));
        var manifest = new VsixManifest("Sample.Extension", "1.0.0", "Sample Publisher", "Sample Extension", null, null, null);
        DateTimeOffset timestampAt = DateTimeOffset.UtcNow;
        var record = new ManagedExtensionRecord(
            "SSMS22",
            manifest,
            null,
            "cached.vsix",
            false,
            DateTimeOffset.UtcNow,
            null,
            ManagedExtensionRecord.UninstalledTimestampKind,
            timestampAt);

        await store.UpsertAsync(record);
        IReadOnlyList<ManagedExtensionRecord> saved = await store.LoadAsync();

        Assert.Single(saved);
        Assert.Equal("Sample.Extension", saved[0].Manifest.Id);
        Assert.Equal(ManagedExtensionRecord.UninstalledTimestampKind, saved[0].TimestampKind);
        Assert.Equal(timestampAt, saved[0].TimestampAt);

        await store.RemoveAsync("SSMS22", "Sample.Extension");
        saved = await store.LoadAsync();

        Assert.Empty(saved);
    }

    [Fact]
    public async Task AppSettingsStore_SavesAndLoadsSettings()
    {
        string tempRoot = CreateTempRoot();
        AppSettingsStore store = new(Path.Combine(tempRoot, "settings.json"));
        var settings = new AppSettings(
            "SSMS22.Test",
            true,
            true,
            new WindowPlacementSettings(120, 240, 1180, 720, true),
            true);

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal("SSMS22.Test", loaded.SelectedSsmsInstanceId);
        Assert.True(loaded.ShowMicrosoftExtensions);
        Assert.True(loaded.DarkTheme);
        Assert.NotNull(loaded.WindowPlacement);
        Assert.Equal(120, loaded.WindowPlacement!.Left);
        Assert.Equal(240, loaded.WindowPlacement.Top);
        Assert.Equal(1180, loaded.WindowPlacement.Width);
        Assert.Equal(720, loaded.WindowPlacement.Height);
        Assert.True(loaded.WindowPlacement.IsMaximized);
        Assert.Equal(AppSettings.ManageViewModeTiles, loaded.ManageViewMode);
    }

    [Fact]
    public async Task AppSettingsStore_DefaultsDarkThemeOffForExistingSettings()
    {
        string tempRoot = CreateTempRoot();
        string settingsPath = Path.Combine(tempRoot, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """
            {
              "SelectedSsmsInstanceId": "SSMS22.Test",
              "ShowMicrosoftExtensions": true
            }
            """);
        AppSettingsStore store = new(settingsPath);

        AppSettings loaded = await store.LoadAsync();

        Assert.Equal("SSMS22.Test", loaded.SelectedSsmsInstanceId);
        Assert.True(loaded.ShowMicrosoftExtensions);
        Assert.False(loaded.DarkTheme);
        Assert.True(loaded.CheckForApplicationUpdates);
        Assert.Equal(AppSettings.ManageViewModeTiles, loaded.ManageViewMode);
        Assert.Null(loaded.WindowPlacement);
    }

    [Fact]
    public async Task ManagedExtensionStore_LoadsLegacyRecordsWithoutTimestampMetadata()
    {
        string tempRoot = CreateTempRoot();
        string path = Path.Combine(tempRoot, "managed-extensions.json");
        await File.WriteAllTextAsync(path, """
            [
              {
                "SsmsInstanceId": "SSMS22",
                "Manifest": {
                  "Id": "Sample.Extension",
                  "Version": "1.0.0",
                  "Publisher": "Sample Publisher",
                  "DisplayName": "Sample Extension",
                  "Description": null,
                  "MoreInfo": null,
                  "ReleaseNotes": null
                },
                "UpdateSource": null,
                "CachedVsixPath": "cached.vsix",
                "IsInstalled": true,
                "LastSeenAt": "2026-06-08T00:00:00+00:00",
                "InstalledVersionOverride": null
              }
            ]
            """);

        ManagedExtensionStore store = new(path);
        IReadOnlyList<ManagedExtensionRecord> records = await store.LoadAsync();

        ManagedExtensionRecord record = Assert.Single(records);
        Assert.Null(record.TimestampKind);
        Assert.Null(record.TimestampAt);
    }

    [Fact]
    public async Task AppSettingsStore_SavesManageViewMode()
    {
        string tempRoot = CreateTempRoot();
        AppSettingsStore store = new(Path.Combine(tempRoot, "settings.json"));
        var settings = new AppSettings(
            "SSMS22.Test",
            true,
            false,
            null,
            true,
            AppSettings.ManageViewModeList);

        await store.SaveAsync(settings);
        AppSettings loaded = await store.LoadAsync();

        Assert.Equal(AppSettings.ManageViewModeList, loaded.ManageViewMode);
    }

    [Fact]
    public async Task AppSettingsStore_NormalizesWindowPlacementPrecision()
    {
        string tempRoot = CreateTempRoot();
        string settingsPath = Path.Combine(tempRoot, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """
            {
              "SelectedSsmsInstanceId": "SSMS22.Test",
              "ShowMicrosoftExtensions": true,
              "DarkTheme": true,
              "WindowPlacement": {
                "Left": 10.123456789,
                "Top": 20.987654321,
                "Width": 1324.0000000001,
                "Height": 742.4000000000001,
                "IsMaximized": false
              },
              "CheckForApplicationUpdates": true
            }
            """);
        AppSettingsStore store = new(settingsPath);

        AppSettings loaded = await store.LoadAsync();
        await store.SaveAsync(loaded);
        AppSettings reloaded = await store.LoadAsync();

        Assert.NotNull(reloaded.WindowPlacement);
        Assert.Equal(10.12, reloaded.WindowPlacement!.Left);
        Assert.Equal(20.99, reloaded.WindowPlacement.Top);
        Assert.Equal(1324.00, reloaded.WindowPlacement.Width);
        Assert.Equal(742.40, reloaded.WindowPlacement.Height);
    }

    private static string CreateTempRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "SsmsExtensionManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static GalleryExtension CreateGalleryExtension(string id, string displayName, string author)
        => new(
            id,
            displayName,
            "Gallery summary.",
            author,
            "1.0.0",
            null,
            new Uri($"https://ssmsgallery.azurewebsites.net/extensions/{id}/extension.vsix"),
            null,
            null,
            null);

    private static Dictionary<string, GalleryExtension> GalleryById(GalleryExtension galleryExtension)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [galleryExtension.Id] = galleryExtension
        };

    private static void CreateVsix(string path, string id, string version, string publisher, string displayName)
    {
        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        ZipArchiveEntry manifest = archive.CreateEntry("extension.vsixmanifest");
        using Stream stream = manifest.Open();
        using StreamWriter writer = new(stream);
        writer.Write($$"""
            <?xml version="1.0" encoding="utf-8"?>
            <PackageManifest Version="2.0.0" xmlns="http://schemas.microsoft.com/developer/vsx-schema/2011">
              <Metadata>
                <Identity Id="{{id}}" Version="{{version}}" Publisher="{{publisher}}" />
                <DisplayName>{{displayName}}</DisplayName>
                <Description>Test extension</Description>
                <MoreInfo>https://example.com</MoreInfo>
              </Metadata>
              <Installation>
                <InstallationTarget Id="Microsoft.VisualStudio.Product.SSMS" Version="[22.0,23.0)" />
              </Installation>
            </PackageManifest>
            """);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
