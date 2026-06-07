using System.Text.Json;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class AppSettingsStore
{
    private readonly string _filePath;

    public AppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SsmsExtensionManager",
            "settings.json");
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return AppSettings.Default;
        }

        await using FileStream stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions.Default, cancellationToken).ConfigureAwait(false)
            ?? AppSettings.Default;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using FileStream stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        using FileStream stream = File.Create(_filePath);
        JsonSerializer.Serialize(stream, settings, JsonOptions.Default);
    }
}
