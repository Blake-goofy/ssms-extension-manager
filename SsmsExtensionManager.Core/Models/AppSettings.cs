namespace SsmsExtensionManager.Core.Models;

public sealed record AppSettings(
    string? SelectedSsmsInstanceId,
    bool ShowMicrosoftExtensions,
    bool DarkTheme,
    WindowPlacementSettings? WindowPlacement,
    bool CheckForApplicationUpdates = true)
{
    public static AppSettings Default { get; } = new(null, false, true, null, true);
}

public sealed record WindowPlacementSettings(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);
