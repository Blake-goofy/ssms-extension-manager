namespace SsmsExtensionManager.Core.Models;

public sealed record AppSettings(
    string? SelectedSsmsInstanceId,
    bool ShowMicrosoftExtensions,
    bool DarkTheme,
    WindowPlacementSettings? WindowPlacement,
    bool CheckForApplicationUpdates = true,
    string ManageViewMode = AppSettings.ManageViewModeTiles,
    string BrowseViewMode = AppSettings.ManageViewModeTiles,
    string? SsmsLaunchExecutablePath = null,
    string SsmsLaunchArguments = "")
{
    public const string ManageViewModeTiles = "Tiles";
    public const string ManageViewModeList = "List";

    public static AppSettings Default { get; } = new(null, false, true, null, true, ManageViewModeTiles, ManageViewModeTiles, null, "");

    public static string NormalizeViewMode(string? value)
        => string.Equals(value, ManageViewModeList, StringComparison.OrdinalIgnoreCase)
            ? ManageViewModeList
            : ManageViewModeTiles;
}

public sealed record WindowPlacementSettings(
    double Left,
    double Top,
    double Width,
    double Height,
    bool IsMaximized);
