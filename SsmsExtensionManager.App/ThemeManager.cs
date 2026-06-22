using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SsmsExtensionManager.App;

internal static class ThemeManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20h1 = 19;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const uint DwmColorNone = 0xFFFFFFFE;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static bool _darkTheme;

    public static void Apply(bool darkTheme)
    {
        _darkTheme = darkTheme;
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
        SetBrush(resources, "TitleBarForegroundBrush", palette.TitleBarForeground);
        SetBrush(resources, "WarningForegroundBrush", palette.WarningForeground);
        SetBrush(resources, "BorderBrush", palette.Border);
        SetBrush(resources, "GridLineBrush", palette.GridLine);
        SetBrush(resources, "HeaderBackgroundBrush", palette.HeaderBackground);
        SetBrush(resources, "SelectionBackgroundBrush", palette.SelectionBackground);
        SetBrush(resources, "SelectionForegroundBrush", palette.SelectionForeground);
        SetBrush(resources, "MenuItemHoverBrush", palette.MenuItemHover);
        SetBrush(resources, "FallbackIconBackgroundBrush", palette.FallbackIconBackground);
        SetBrush(resources, "UnavailableRowBackgroundBrush", palette.UnavailableRowBackground);
        SetBrush(resources, "UnavailableRowForegroundBrush", palette.UnavailableRowForeground);
        SetBrush(resources, "HyperlinkBrush", palette.Hyperlink);
        SetBrush(resources, "PrimaryActionBrush", palette.PrimaryAction);
        SetBrush(resources, "PrimaryActionHoverBrush", palette.PrimaryActionHover);
        SetBrush(resources, "PrimaryActionPressedBrush", palette.PrimaryActionPressed);
        SetBrush(resources, "PrimaryActionForegroundBrush", palette.PrimaryActionForeground);
        SetBrush(resources, "LaunchActionBrush", palette.LaunchAction);
        SetBrush(resources, "LaunchActionHoverBrush", palette.LaunchActionHover);
        SetBrush(resources, "LaunchActionPressedBrush", palette.LaunchActionPressed);
        SetBrush(resources, "LaunchActionForegroundBrush", palette.LaunchActionForeground);

        foreach (Window window in Application.Current.Windows)
        {
            ApplyWindowChrome(window);
        }
    }

    public static void RegisterWindow(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyWindowChrome(window);
        window.Loaded += (_, _) => ApplyWindowChrome(window);
        window.StateChanged += (_, _) => ApplyWindowChrome(window);
    }

    private static void SetBrush(ResourceDictionary resources, object key, Color color)
    {
        resources[key] = new SolidColorBrush(color);
    }

    private static void ApplyWindowChrome(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            int enabled = _darkTheme ? 1 : 0;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeBefore20h1, ref enabled, sizeof(int));
            }
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            WindowFrameAppearance frameAppearance = GetWindowFrameAppearance(_darkTheme, window.WindowState == WindowState.Maximized);
            int cornerPreference = (int)frameAppearance.CornerPreference;
            uint borderColor = frameAppearance.BorderColor;
            _ = DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
            _ = DwmSetWindowAttribute(handle, DwmwaBorderColor, ref borderColor, sizeof(uint));
        }

        _ = SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoOwnerZOrder | SwpNoActivate | SwpFrameChanged);
    }

    internal static WindowFrameAppearance GetWindowFrameAppearance(bool darkTheme, bool isMaximized)
    {
        if (isMaximized)
        {
            return new WindowFrameAppearance(DwmWindowCornerPreference.DoNotRound, DwmColorNone);
        }

        ThemePalette palette = darkTheme ? ThemePalette.Dark : ThemePalette.Light;
        return new WindowFrameAppearance(DwmWindowCornerPreference.Round, ToColorRef(palette.WindowOutline));
    }

    internal static uint ToColorRef(Color color) => (uint)(color.R | (color.G << 8) | (color.B << 16));

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int attributeSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref uint value, int attributeSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    internal readonly record struct WindowFrameAppearance(DwmWindowCornerPreference CornerPreference, uint BorderColor);

    internal enum DwmWindowCornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3
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
        Color TitleBarForeground,
        Color WarningForeground,
        Color Border,
        Color GridLine,
        Color HeaderBackground,
        Color SelectionBackground,
        Color SelectionForeground,
        Color MenuItemHover,
        Color FallbackIconBackground,
        Color UnavailableRowBackground,
        Color UnavailableRowForeground,
        Color Hyperlink,
        Color WindowOutline,
        Color PrimaryAction,
        Color PrimaryActionHover,
        Color PrimaryActionPressed,
        Color PrimaryActionForeground,
        Color LaunchAction,
        Color LaunchActionHover,
        Color LaunchActionPressed,
        Color LaunchActionForeground)
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
            Color.FromRgb(0x5B, 0x64, 0x73),
            Color.FromRgb(0x7A, 0x3E, 0x00),
            Color.FromRgb(0xD1, 0xD5, 0xDB),
            Color.FromRgb(0xE5, 0xE7, 0xEB),
            Color.FromRgb(0xF3, 0xF4, 0xF6),
            Color.FromRgb(0xE8, 0xF1, 0xFF),
            Color.FromRgb(0x1F, 0x29, 0x37),
            Color.FromRgb(0xEA, 0xF2, 0xFF),
            Color.FromRgb(0xE5, 0xE7, 0xEB),
            Color.FromRgb(0xF5, 0xF5, 0xF5),
            Color.FromRgb(0x6B, 0x72, 0x80),
            Color.FromRgb(0x0B, 0x57, 0xD0),
            Color.FromRgb(0xE1, 0xE5, 0xEA),
            Color.FromRgb(0x0E, 0x63, 0x9C),
            Color.FromRgb(0x11, 0x77, 0xBB),
            Color.FromRgb(0x0B, 0x57, 0x8A),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0x11, 0x11, 0x11),
            Color.FromRgb(0x25, 0x25, 0x25),
            Color.FromRgb(0x00, 0x00, 0x00),
            Color.FromRgb(0xFF, 0xFF, 0xFF));

        public static ThemePalette Dark { get; } = new(
            Color.FromRgb(0x10, 0x18, 0x24),
            Color.FromRgb(0x16, 0x20, 0x2E),
            Color.FromRgb(0x1D, 0x29, 0x3A),
            Color.FromRgb(0xE5, 0xE7, 0xEB),
            Color.FromRgb(0x29, 0x38, 0x4D),
            Color.FromRgb(0x33, 0x45, 0x5F),
            Color.FromRgb(0x18, 0x24, 0x33),
            Color.FromRgb(0xB8, 0xC2, 0xCF),
            Color.FromRgb(0x98, 0xA2, 0xB3),
            Color.FromRgb(0xF7, 0xC5, 0x66),
            Color.FromRgb(0x3B, 0x4A, 0x5E),
            Color.FromRgb(0x2D, 0x3A, 0x4A),
            Color.FromRgb(0x20, 0x2C, 0x3D),
            Color.FromRgb(0x24, 0x32, 0x46),
            Color.FromRgb(0xE5, 0xE7, 0xEB),
            Color.FromRgb(0x2D, 0x40, 0x59),
            Color.FromRgb(0x33, 0x41, 0x55),
            Color.FromRgb(0x18, 0x24, 0x33),
            Color.FromRgb(0xA7, 0xB1, 0xC2),
            Color.FromRgb(0x8A, 0xC7, 0xFF),
            Color.FromRgb(0x2C, 0x37, 0x46),
            Color.FromRgb(0x0E, 0x63, 0x9C),
            Color.FromRgb(0x11, 0x77, 0xBB),
            Color.FromRgb(0x0B, 0x57, 0x8A),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xFF, 0xFF, 0xFF),
            Color.FromRgb(0xE5, 0xE7, 0xEB),
            Color.FromRgb(0xD1, 0xD5, 0xDB),
            Color.FromRgb(0x11, 0x11, 0x11));
    }
}
