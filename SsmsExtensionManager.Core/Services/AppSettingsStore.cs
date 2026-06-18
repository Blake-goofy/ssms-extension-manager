using System.Text.Json;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class AppSettingsStore
{
    private readonly string _filePath;

    public AppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.SettingsFilePath;
    }

    public string FilePath => _filePath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return AppSettings.Default;
        }

        await using FileStream stream = File.OpenRead(_filePath);
        AppSettings settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions.Default, cancellationToken).ConfigureAwait(false)
            ?? AppSettings.Default;
        return Normalize(settings);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using FileStream stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, Normalize(settings), JsonOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        using FileStream stream = File.Create(_filePath);
        JsonSerializer.Serialize(stream, Normalize(settings), JsonOptions.Default);
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        settings = settings with
        {
            ManageViewMode = AppSettings.NormalizeViewMode(settings.ManageViewMode),
            BrowseViewMode = AppSettings.NormalizeViewMode(settings.BrowseViewMode),
            SsmsLaunchExecutablePath = ValueNormalization.EmptyToNull(settings.SsmsLaunchExecutablePath),
            SsmsLaunchArguments = settings.SsmsLaunchArguments ?? string.Empty
        };

        if (settings.WindowPlacement is not { } placement)
        {
            return settings;
        }

        return settings with
        {
            WindowPlacement = placement with
            {
                Left = ValueNormalization.RoundWindowPlacement(placement.Left),
                Top = ValueNormalization.RoundWindowPlacement(placement.Top),
                Width = ValueNormalization.RoundWindowPlacement(placement.Width),
                Height = ValueNormalization.RoundWindowPlacement(placement.Height)
            }
        };
    }
}
