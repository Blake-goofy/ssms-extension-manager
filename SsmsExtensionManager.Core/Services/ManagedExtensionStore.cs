using System.Collections.Concurrent;
using System.Text.Json;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class ManagedExtensionStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock;

    public ManagedExtensionStore(string? filePath = null)
    {
        _filePath = filePath ?? AppPaths.ManagedExtensionsFilePath;
        _fileLock = FileLocks.GetOrAdd(Path.GetFullPath(_filePath), _ => new SemaphoreSlim(1, 1));
    }

    public async Task<IReadOnlyList<ManagedExtensionRecord>> LoadAsync(CancellationToken cancellationToken = default)
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

    public async Task SaveAsync(IEnumerable<ManagedExtensionRecord> records, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await SaveUnlockedAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task UpsertAsync(ManagedExtensionRecord record, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ManagedExtensionRecord> records = [.. await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false)];
            records.RemoveAll(existing => string.Equals(RecordKey(existing), RecordKey(record), StringComparison.OrdinalIgnoreCase));
            records.Add(record);
            await SaveUnlockedAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task RemoveAsync(string ssmsInstanceId, string vsixId, CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            List<ManagedExtensionRecord> records = [.. await LoadUnlockedAsync(cancellationToken).ConfigureAwait(false)];
            records.RemoveAll(existing =>
                string.Equals(existing.SsmsInstanceId, ssmsInstanceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Manifest.Id, vsixId, StringComparison.OrdinalIgnoreCase));
            await SaveUnlockedAsync(records, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task<IReadOnlyList<ManagedExtensionRecord>> LoadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using FileStream stream = File.OpenRead(_filePath);
        List<ManagedExtensionRecord>? records = await JsonSerializer.DeserializeAsync<List<ManagedExtensionRecord>>(stream, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
        return records ?? [];
    }

    private async Task SaveUnlockedAsync(IEnumerable<ManagedExtensionRecord> records, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        List<ManagedExtensionRecord> orderedRecords = records
            .GroupBy(RecordKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(record => record.LastSeenAt).First())
            .OrderBy(record => record.Manifest.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        await using FileStream stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, orderedRecords, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
    }

    public static string RecordKey(ManagedExtensionRecord record) => $"{record.SsmsInstanceId}|{record.Manifest.Id}";
}
