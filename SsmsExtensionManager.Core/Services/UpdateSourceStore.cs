using System.Text.Json;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class UpdateSourceStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock;

    public UpdateSourceStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.ExtensionSourcesFilePath;
        _fileLock = NamedFileLock.GetOrAdd(_filePath);
    }

    public async Task<IReadOnlyDictionary<string, UpdateSource>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, UpdateSource> sources, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveUnlockedAsync(sources, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SetAsync(string vsixId, UpdateSource source, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, UpdateSource> sources = new(await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase)
            {
                [vsixId] = source
            };

            await SaveUnlockedAsync(sources, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RemoveAsync(string vsixId, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Dictionary<string, UpdateSource> sources = new(await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
            sources.Remove(vsixId);
            await SaveUnlockedAsync(sources, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, UpdateSource>> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, UpdateSource>(StringComparer.OrdinalIgnoreCase);
        }

        await using FileStream stream = File.OpenRead(_filePath);
        Dictionary<string, UpdateSource>? sources = await JsonSerializer.DeserializeAsync<Dictionary<string, UpdateSource>>(stream, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
        return NormalizeSources(sources ?? []);
    }

    private async Task SaveUnlockedAsync(IReadOnlyDictionary<string, UpdateSource> sources, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        Dictionary<string, UpdateSource> normalizedSources = NormalizeSources(sources);
        await using FileStream stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, normalizedSources, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, UpdateSource> NormalizeSources(IEnumerable<KeyValuePair<string, UpdateSource>> sources)
    {
        Dictionary<string, UpdateSource> normalizedSources = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string vsixId, UpdateSource source) in sources)
        {
            if (UpdateSourceSanitizer.Normalize(source) is { } normalizedSource)
            {
                normalizedSources[vsixId] = normalizedSource;
            }
        }

        return normalizedSources;
    }
}
