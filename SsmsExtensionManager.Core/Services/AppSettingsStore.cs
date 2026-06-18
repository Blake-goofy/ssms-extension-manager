using System.Text.Json;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class AppSettingsStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _ioGate = new(1, 1);

    public AppSettingsStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.SettingsFilePath;
    }

    public string FilePath => _filePath;

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
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
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _ioGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveCoreAsync(Normalize(settings), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public void Save(AppSettings settings)
    {
        _ioGate.Wait();
        try
        {
            SaveCore(Normalize(settings));
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private async Task SaveCoreAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        string directoryPath = GetDirectoryPath();
        Directory.CreateDirectory(directoryPath);
        string tempFilePath = GetTempFilePath(directoryPath);

        try
        {
            await using (FileStream stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ReplaceSettingsFile(tempFilePath);
        }
        catch
        {
            TryDeleteTempFile(tempFilePath);
            throw;
        }
    }

    private void SaveCore(AppSettings settings)
    {
        string directoryPath = GetDirectoryPath();
        Directory.CreateDirectory(directoryPath);
        string tempFilePath = GetTempFilePath(directoryPath);

        try
        {
            using (FileStream stream = File.Create(tempFilePath))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions.Default);
                stream.Flush();
            }

            ReplaceSettingsFile(tempFilePath);
        }
        catch
        {
            TryDeleteTempFile(tempFilePath);
            throw;
        }
    }

    private string GetDirectoryPath() => Path.GetDirectoryName(Path.GetFullPath(_filePath))!;

    private string GetTempFilePath(string directoryPath)
        => Path.Combine(directoryPath, $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

    private void ReplaceSettingsFile(string tempFilePath)
    {
        // Replace the file only after the new JSON is fully written to avoid partial reads.
        if (File.Exists(_filePath))
        {
            File.Replace(tempFilePath, _filePath, destinationBackupFileName: null);
            return;
        }

        File.Move(tempFilePath, _filePath);
    }

    private static void TryDeleteTempFile(string tempFilePath)
    {
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch
        {
        }
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
