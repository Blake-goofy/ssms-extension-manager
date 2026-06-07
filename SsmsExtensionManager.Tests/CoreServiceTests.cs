using System.IO.Compression;
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
    public async Task ManagedExtensionStore_UpsertsAndRemovesRecords()
    {
        string tempRoot = CreateTempRoot();
        ManagedExtensionStore store = new(Path.Combine(tempRoot, "managed-extensions.json"));
        var manifest = new VsixManifest("Sample.Extension", "1.0.0", "Sample Publisher", "Sample Extension", null, null, null);
        var record = new ManagedExtensionRecord("SSMS22", manifest, null, "cached.vsix", false, DateTimeOffset.UtcNow);

        await store.UpsertAsync(record);
        IReadOnlyList<ManagedExtensionRecord> saved = await store.LoadAsync();

        Assert.Single(saved);
        Assert.Equal("Sample.Extension", saved[0].Manifest.Id);

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
            new WindowPlacementSettings(120, 240, 1180, 720, true));

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
        Assert.Null(loaded.WindowPlacement);
    }

    private static string CreateTempRoot()
    {
        string path = Path.Combine(Path.GetTempPath(), "SsmsExtensionManager.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

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
}
