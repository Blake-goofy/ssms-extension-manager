using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace SsmsExtensionManager.App;

internal static class WpfUiHelpers
{
    public static void UpdatePlaceholderVisibility(TextBlock? placeholder, TextBox? textBox)
    {
        if (placeholder is null || textBox is null)
        {
            return;
        }

        placeholder.Visibility = string.IsNullOrWhiteSpace(textBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public static void ClosePopupIfClickOutside(Popup? popup, params FrameworkElement?[] ignoredElements)
    {
        if (popup?.IsOpen != true)
        {
            return;
        }

        if (ignoredElements.Any(element => element?.IsMouseOver == true))
        {
            return;
        }

        popup.IsOpen = false;
    }
}
