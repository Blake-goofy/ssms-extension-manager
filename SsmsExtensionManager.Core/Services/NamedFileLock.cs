using System.Collections.Concurrent;

namespace SsmsExtensionManager.Core.Services;

internal static class NamedFileLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.OrdinalIgnoreCase);

    public static SemaphoreSlim GetOrAdd(string filePath)
        => Locks.GetOrAdd(Path.GetFullPath(filePath), _ => new SemaphoreSlim(1, 1));
}
