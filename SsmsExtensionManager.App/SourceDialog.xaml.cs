using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SsmsExtensionManager.Core.Models;
using SsmsExtensionManager.Core.Services;

namespace SsmsExtensionManager.App;

public partial class SourceDialog : Window
{
    private GitHubRepository? _repository;

    public SourceDialog(string initialUri)
    {
        InitializeComponent();
        SourceUriTextBox.Text = GitHubRepository.TryParse(initialUri, out GitHubRepository repository)
            ? repository.ToString()
            : initialUri;
        UpdatePlaceholderVisibility();
    }

    public UpdateSourceType SelectedSourceType => UpdateSourceType.GitHubRelease;

    public string SourceUri => _repository?.ToString() ?? SourceUriTextBox.Text.Trim();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            SourceUriTextBox.Focus();
            SourceUriTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void SourceUriTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePlaceholderVisibility();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceUriTextBox.Text))
        {
            MessageBox.Show(this, "Enter a GitHub repository.", "Set Update Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!GitHubRepository.TryParse(SourceUriTextBox.Text, out GitHubRepository repository))
        {
            MessageBox.Show(this, "Enter a valid GitHub repository.", "Set Update Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _repository = repository;
        SourceUriTextBox.Text = repository.ToString();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdatePlaceholderVisibility()
    {
        SourcePlaceholderText.Visibility = string.IsNullOrWhiteSpace(SourceUriTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
