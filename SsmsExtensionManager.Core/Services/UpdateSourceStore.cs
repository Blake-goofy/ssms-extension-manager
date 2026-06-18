using System.Text.Json;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class UpdateSourceStore
{
    private readonly string _filePath;

    public UpdateSourceStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.ExtensionSourcesFilePath;
    }

    public async Task<IReadOnlyDictionary<string, UpdateSource>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, UpdateSource>(StringComparer.OrdinalIgnoreCase);
        }

        await using FileStream stream = File.OpenRead(_filePath);
        Dictionary<string, UpdateSource>? sources = await JsonSerializer.DeserializeAsync<Dictionary<string, UpdateSource>>(stream, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
        return new Dictionary<string, UpdateSource>(sources ?? [], StringComparer.OrdinalIgnoreCase);
    }

    public async Task SaveAsync(IReadOnlyDictionary<string, UpdateSource> sources, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        await using FileStream stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, sources, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetAsync(string vsixId, UpdateSource source, CancellationToken cancellationToken = default)
    {
        Dictionary<string, UpdateSource> sources = new(await LoadAsync(cancellationToken).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase)
        {
            [vsixId] = source
        };

        await SaveAsync(sources, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string vsixId, CancellationToken cancellationToken = default)
    {
        Dictionary<string, UpdateSource> sources = new(await LoadAsync(cancellationToken).ConfigureAwait(false), StringComparer.OrdinalIgnoreCase);
        sources.Remove(vsixId);
        await SaveAsync(sources, cancellationToken).ConfigureAwait(false);
    }
}
