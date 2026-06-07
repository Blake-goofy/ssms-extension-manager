using System.Windows;
using SsmsExtensionManager.Core.Models;
using SsmsExtensionManager.Core.Services;

namespace SsmsExtensionManager.App;

public partial class SourceDialog : Window
{
    private GitHubRepository? _repository;

    public SourceDialog(string initialUri)
    {
        InitializeComponent();
        SourceUriTextBox.Text = initialUri;
    }

    public UpdateSourceType SelectedSourceType => UpdateSourceType.GitHubRelease;

    public string SourceUri => _repository?.RepositoryUri.ToString() ?? SourceUriTextBox.Text.Trim();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SourceUriTextBox.Text))
        {
            MessageBox.Show(this, "Enter a GitHub repository, for example Axial-SQL/AxialSqlTools.", "Set Update Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!GitHubRepository.TryParse(SourceUriTextBox.Text, out GitHubRepository repository))
        {
            MessageBox.Show(this, "Enter a GitHub repository, for example Axial-SQL/AxialSqlTools or https://github.com/owner/repo.", "Set Update Source", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _repository = repository;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
