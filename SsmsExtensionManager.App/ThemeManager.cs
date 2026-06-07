using System.Windows;
using System.Windows.Media;

namespace SsmsExtensionManager.App;

internal static class ThemeManager
{
    public static void Apply(bool darkTheme)
    {
        ResourceDictionary resources = Application.Current.Resources;
        ThemePalette palette = darkTheme ? ThemePalette.Dark : ThemePalette.Light;

        SetBrush(resources, "AppBackgroundBrush", palette.AppBackground);
        SetBrush(resources, "PanelBackgroundBrush", palette.PanelBackground);
        SetBrush(resources, "ControlBackgroundBrush", palette.ControlBackground);
        SetBrush(resources, "ControlForegroundBrush", palette.ControlForeground);
        SetBrush(resources, "ButtonHoverBrush", palette.ButtonHover);
        SetBrush(resources, "ButtonPressedBrush", palette.ButtonPressed);
        SetBrush(resources, "DisabledControlBackgroundBrush", palette.DisabledControlBackground);
        SetBrush(resources, "MutedForegroundBrush", palette.MutedForeground);
        SetBrush(resources, "WarningForegroundBrush", palette.WarningForeground);
        SetBrush(resources, "BorderBrush", palette.Border);
        SetBrush(resources, "GridLineBrush", palette.GridLine);
        SetBrush(resources, "HeaderBackgroundBrush", palette.HeaderBackground);
        SetBrush(resources, "SelectionBackgroundBrush", palette.SelectionBackground);
        SetBrush(resources, "SelectionForegroundBrush", palette.SelectionForeground);
        SetBrush(resources, "MenuItemHoverBrush", palette.MenuItemHover);
        SetBrush(resources, "UnavailableRowBackgroundBrush", palette.UnavailableRowBackground);
        SetBrush(resources, "UnavailableRowForegroundBrush", palette.UnavailableRowForeground);
        SetBrush(resources, "HyperlinkBrush", palette.Hyperlink);
    }

    private static void SetBrush(ResourceDictionary resources, object key, Color color)
    {
        resources[key] = new SolidColorBrush(color);
    }

    private sealed record ThemePalette(
        Color AppBackground,
        Color PanelBackground,
        Color ControlBackground,
        Color ControlForeground,
        Color ButtonHover,
        Color ButtonPressed,
        Color DisabledControlBackground,
        Color MutedForeground,
        Color WarningForeground,
        Color Border,
        Color GridLine,
        Color HeaderBackground,
        Color SelectionBackground,
        Color SelectionForeground,
        Color MenuItemHover,
        Color UnavailableRowBackground,
        Color UnavailableRowForeground,
        Color Hyperlink)
    {
        public static ThemePalette Light { get; } = new(
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xF7, 0xF8, 0xFA),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0x1F, 0x29, 0x37),
            Color.FromRgb(0xEE, 0xF4, 0xFF),
            Color.FromRgb(0xDC, 0xEB, 0xFF),
            Color.FromRgb(0xF3, 0xF4, 0xF6),
            Color.FromRgb(0x4B, 0x55, 0x63),
            Color.FromRgb(0x7A, 0x3E, 0x00),
            Color.FromRgb(0xD1, 0xD5, 0xDB),
            Color.FromRgb(0xE5, 0xE7, 0xEB),
            Color.FromRgb(0xF3, 0xF4, 0xF6),
            Color.FromRgb(0xE8, 0xF1, 0xFF),
            Color.FromRgb(0x1F, 0x29, 0x37),
            Color.FromRgb(0xEA, 0xF2, 0xFF),
            Color.FromRgb(0xF5, 0xF5, 0xF5),
            Color.FromRgb(0x6B, 0x72, 0x80),
            Color.FromRgb(0x0B, 0x57, 0xD0));

        public static ThemePalette Dark { get; } = new(
            Color.FromRgb(0x10, 0x18, 0x24),
            Color.FromRgb(0x16, 0x20, 0x2E),
            Color.FromRgb(0x1D, 0x29, 0x3A),
            Color.FromRgb(0xE5, 0xE7, 0xEB),
            Color.FromRgb(0x29, 0x38, 0x4D),
            Color.FromRgb(0x33, 0x45, 0x5F),
            Color.FromRgb(0x18, 0x24, 0x33),
            Color.FromRgb(0xB8, 0xC2, 0xCF),
            Color.FromRgb(0xF7, 0xC5, 0x66),
            Color.FromRgb(0x3B, 0x4A, 0x5E),
            Color.FromRgb(0x2D, 0x3A, 0x4A),
            Color.FromRgb(0x20, 0x2C, 0x3D),
            Color.FromRgb(0x24, 0x32, 0x46),
            Color.FromRgb(0xE5, 0xE7, 0xEB),
            Color.FromRgb(0x2D, 0x40, 0x59),
            Color.FromRgb(0x18, 0x24, 0x33),
            Color.FromRgb(0xA7, 0xB1, 0xC2),
            Color.FromRgb(0x8A, 0xC7, 0xFF));
    }
}
