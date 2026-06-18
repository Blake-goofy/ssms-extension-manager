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
}
