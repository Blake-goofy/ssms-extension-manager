using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SsmsExtensionManager.App;

public partial class SettingsDialog : Window
{
    public SettingsDialog(ObservableCollection<InstanceRow> instances, InstanceRow? selectedInstance, bool showMicrosoftExtensions, bool darkTheme)
    {
        InitializeComponent();
        InstanceListBox.ItemsSource = instances;
        InstanceListBox.SelectedItem = selectedInstance ?? instances.FirstOrDefault();
        InstanceDropDownButton.IsEnabled = instances.Count > 0;
        UpdateInstanceDropDownText();
        ShowMicrosoftExtensionsCheckBox.IsChecked = showMicrosoftExtensions;
        DarkThemeCheckBox.IsChecked = darkTheme;
    }

    public InstanceRow? SelectedInstance => InstanceListBox.SelectedItem as InstanceRow;

    public bool ShowMicrosoftExtensions => ShowMicrosoftExtensionsCheckBox.IsChecked == true;

    public bool DarkTheme => DarkThemeCheckBox.IsChecked == true;

    private void InstanceDropDown_Click(object sender, RoutedEventArgs e)
    {
        InstanceDropDownPopup.IsOpen = !InstanceDropDownPopup.IsOpen;
    }

    private void InstanceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateInstanceDropDownText();

        if (IsLoaded)
        {
            InstanceDropDownPopup.IsOpen = false;
        }
    }

    private void UpdateInstanceDropDownText()
    {
        InstanceDropDownText.Text = SelectedInstance?.Display ?? "No SSMS 22 instance detected";
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!InstanceDropDownPopup.IsOpen)
        {
            return;
        }

        if (InstanceDropDownButton.IsMouseOver || InstanceListBox.IsMouseOver)
        {
            return;
        }

        InstanceDropDownPopup.IsOpen = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedInstance is null)
        {
            MessageBox.Show(this, "No SSMS 22 instance is available.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
