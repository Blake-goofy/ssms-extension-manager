using System.Text.Json;

namespace SsmsExtensionManager.Core.Services;

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
