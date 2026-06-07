using System.Text.Json;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class ManagedExtensionStore
{
    private readonly string _filePath;

    public ManagedExtensionStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SsmsExtensionManager",
            "managed-extensions.json");
    }

    public async Task<IReadOnlyList<ManagedExtensionRecord>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using FileStream stream = File.OpenRead(_filePath);
        List<ManagedExtensionRecord>? records = await JsonSerializer.DeserializeAsync<List<ManagedExtensionRecord>>(stream, JsonOptions.Default, cancellationToken).ConfigureAwait(false);
        return records ?? [];
    }

    public async Task SaveAsync(IEnumerable<ManagedExtensionRecord> records, CancellationToken cancellationToken = default)
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

    public async Task UpsertAsync(ManagedExtensionRecord record, CancellationToken cancellationToken = default)
    {
        List<ManagedExtensionRecord> records = [.. await LoadAsync(cancellationToken).ConfigureAwait(false)];
        records.RemoveAll(existing => string.Equals(RecordKey(existing), RecordKey(record), StringComparison.OrdinalIgnoreCase));
        records.Add(record);
        await SaveAsync(records, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string ssmsInstanceId, string vsixId, CancellationToken cancellationToken = default)
    {
        List<ManagedExtensionRecord> records = [.. await LoadAsync(cancellationToken).ConfigureAwait(false)];
        records.RemoveAll(existing =>
            string.Equals(existing.SsmsInstanceId, ssmsInstanceId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.Manifest.Id, vsixId, StringComparison.OrdinalIgnoreCase));
        await SaveAsync(records, cancellationToken).ConfigureAwait(false);
    }

    public static string RecordKey(ManagedExtensionRecord record) => $"{record.SsmsInstanceId}|{record.Manifest.Id}";
}
