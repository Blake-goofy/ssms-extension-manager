using System.Windows.Media;
using SsmsExtensionManager.App;

namespace SsmsExtensionManager.Tests;

public sealed class ThemeManagerTests
{
    [Fact]
    public void GetWindowFrameAppearance_RoundsNormalDarkWindowsWithLightOutline()
    {
        ThemeManager.WindowFrameAppearance appearance = ThemeManager.GetWindowFrameAppearance(darkTheme: true, isMaximized: false);

        Assert.Equal(ThemeManager.DwmWindowCornerPreference.Round, appearance.CornerPreference);
        Assert.Equal(ThemeManager.ToColorRef(Color.FromRgb(0x2C, 0x37, 0x46)), appearance.BorderColor);
    }

    [Fact]
    public void GetWindowFrameAppearance_DisablesRoundedOutlineWhenMaximized()
    {
        ThemeManager.WindowFrameAppearance appearance = ThemeManager.GetWindowFrameAppearance(darkTheme: false, isMaximized: true);

        Assert.Equal(ThemeManager.DwmWindowCornerPreference.DoNotRound, appearance.CornerPreference);
        Assert.Equal(0xFFFFFFFEu, appearance.BorderColor);
    }
}
