namespace SsmsExtensionManager.Core.Services;

public static class ValueNormalization
{
    public static string? EmptyToNull(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static double RoundWindowPlacement(double value)
        => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static string PublisherKey(string publisher)
    {
        string key = publisher.Trim().ToUpperInvariant();
        foreach (string token in new[] { " CORPORATION", " CORP.", " CORP" })
        {
            if (key.EndsWith(token, StringComparison.Ordinal))
            {
                key = key[..^token.Length].TrimEnd();
                break;
            }
        }

        return key;
    }
}
