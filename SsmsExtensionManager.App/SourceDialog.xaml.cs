using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SsmsExtensionManager.Core.Models;
using SsmsExtensionManager.Core.Services;

namespace SsmsExtensionManager.App;

public partial class SourceDialog : Window
{
    private GitHubRepository? _repository;
    private UpdateSourceType _selectedSourceType;

    public SourceDialog(string initialUri)
    {
        InitializeComponent();
        SourceUriTextBox.Text = GitHubRepository.TryParse(initialUri, out GitHubRepository repository)
            ? repository.ToString()
            : initialUri;
        UpdatePlaceholderVisibility();
    }

    public UpdateSourceType SelectedSourceType => _selectedSourceType;

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
            MessageBox.Show(this, "Enter a repository or downloadable VSIX/ZIP link.", "Edit Update Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (GitHubRepository.TryParse(SourceUriTextBox.Text, out GitHubRepository repository))
        {
            _repository = repository;
            _selectedSourceType = UpdateSourceType.GitHubRelease;
            SourceUriTextBox.Text = repository.ToString();
            DialogResult = true;
            return;
        }

        if (TryGetDirectSourceType(SourceUriTextBox.Text, out UpdateSourceType sourceType))
        {
            _repository = null;
            _selectedSourceType = sourceType;
            DialogResult = true;
            return;
        }

        MessageBox.Show(this, "Enter a valid GitHub repository or direct .vsix/.zip URL.", "Edit Update Source", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private static bool TryGetDirectSourceType(string value, out UpdateSourceType sourceType)
    {
        sourceType = UpdateSourceType.Unknown;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        string extension = Path.GetExtension(uri.LocalPath);
        if (extension.Equals(".vsix", StringComparison.OrdinalIgnoreCase))
        {
            sourceType = UpdateSourceType.DirectVsixUrl;
            return true;
        }

        if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            sourceType = UpdateSourceType.DirectZipUrl;
            return true;
        }

        return false;
    }
}
