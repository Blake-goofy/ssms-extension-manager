namespace SsmsExtensionManager.Core.Models;

public sealed record AppSettings(
    string? SelectedSsmsInstanceId,
    bool ShowMicrosoftExtensions,
    bool DarkTheme,
    WindowPlacementSettings? WindowPlacement)
{
    public static AppSettings Default { get; } = new(null, false, false, null);
}

public sealed record WindowPlacementSettings(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);
