using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using Microsoft.Win32;
using SsmsExtensionManager.Core.Models;
using SsmsExtensionManager.Core.Services;

namespace SsmsExtensionManager.App;

public partial class MainWindow : Window
{
    private enum NavigationView
    {
        Manage,
        Browse,
        Settings
    }

    private readonly SsmsInstanceDetector _instanceDetector = new();
    private readonly VsixManifestReader _manifestReader = new();
    private readonly UpdateSourceStore _sourceStore = new();
    private readonly ManagedExtensionStore _managedStore = new();
    private readonly PackageCache _packageCache = new();
    private readonly AppSettingsStore _settingsStore = new();
    private readonly AppUpdateService _appUpdateService = new();
    private readonly HttpClient _httpClient = new();
    private readonly ObservableCollection<InstanceRow> _instances = [];
    private readonly ObservableCollection<ExtensionRow> _extensions = [];
    private readonly List<ExtensionRow> _allExtensions = [];
    private readonly InstalledExtensionScanner _scanner;
    private readonly ExtensionAssetResolver _assetResolver;
    private readonly ExtensionInstaller _installer;
    private readonly GitHubReleaseUpdateChecker _updateChecker;
    private Point _pendingRowActionsPoint;
    private AppUpdateCheckResult? _applicationUpdateResult;
    private InstanceRow? _selectedInstance;
    private NavigationView _currentView = NavigationView.Manage;
    private bool _isInitializingSettingsControls;
    private bool _checkForApplicationUpdates = true;
    private bool _showMicrosoftExtensions;
    private bool _darkTheme;
    private string? _preferredInstanceId;

    public MainWindow()
    {
        InitializeComponent();
        _assetResolver = new ExtensionAssetResolver(_manifestReader);
        _scanner = new InstalledExtensionScanner(_manifestReader, _sourceStore);
        _installer = new ExtensionInstaller(_assetResolver);
        _updateChecker = new GitHubReleaseUpdateChecker(_httpClient, _assetResolver);
        ExtensionsGrid.ItemsSource = _extensions;
        UpdateSelectionActionState();
        Title = $"SSMS Extension Manager {AppBuildInfo.Version}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppSettings settings = await _settingsStore.LoadAsync();
        _showMicrosoftExtensions = settings.ShowMicrosoftExtensions;
        _darkTheme = settings.DarkTheme;
        _checkForApplicationUpdates = settings.CheckForApplicationUpdates;
        _preferredInstanceId = settings.SelectedSsmsInstanceId;
        ThemeManager.Apply(_darkTheme);
        _isInitializingSettingsControls = true;
        ShowMicrosoftExtensionsCheckBox.IsChecked = _showMicrosoftExtensions;
        DarkThemeCheckBox.IsChecked = _darkTheme;
        CheckForAppUpdatesCheckBox.IsChecked = _checkForApplicationUpdates;
        _isInitializingSettingsControls = false;
        ApplyWindowPlacement(settings.WindowPlacement);
        await RefreshAsync();
        ApplyNavigation();
        if (_checkForApplicationUpdates)
        {
            await RefreshApplicationUpdateStateAsync(interactive: false, promptToApply: false);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CloseRowActionsPopup();
        CloseInstanceDropDownIfNeeded(e);
    }

    private async void InstallLocal_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedInstance is not { } instance)
        {
            ShowMessage("No SSMS 22 instance is selected. Open View > Settings to choose an instance.");
            return;
        }

        OpenFileDialog dialog = new()
        {
            Filter = "VSIX or ZIP (*.vsix;*.zip)|*.vsix;*.zip|All files (*.*)|*.*",
            Title = "Install SSMS extension"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await RunBusyAsync("Installing extension...", async () =>
        {
            ExtensionAsset asset = _assetResolver.Resolve(dialog.FileName, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
            string cachedVsix = _packageCache.CacheVsix(asset.FilePath, asset.Manifest);
                OperationResult result = await Task.Run(() => _installer.InstallLocalAsset(instance.Instance, cachedVsix));
                if (result.Success)
                {
                    await SaveRecordAsync(instance.Instance, asset.Manifest, null, cachedVsix, isInstalled: true, installedVersionOverride: null);
                }
            else
            {
                ShowMessage(result.Message);
            }

            await LoadExtensionsAsync(checkUpdates: true);
        });
    }

    private async void UpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        List<ExtensionRow> selected = ExtensionsGrid.SelectedItems.Cast<ExtensionRow>().ToList();
        if (selected.Count == 0)
        {
            ShowMessage("Select one or more installed extensions to update.");
            return;
        }

        await UpdateRowsAsync(selected, reportNoUpdate: true);
    }

    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        List<ExtensionRow> rows = _extensions.Where(row => row.IsInstalled && row.AvailableUpdate is not null).ToList();
        if (rows.Count == 0)
        {
            ShowMessage("No updates are available.");
            return;
        }

        await UpdateRowsAsync(rows, reportNoUpdate: false);
    }

    private async void Reinstall_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        List<ExtensionRow> selected = ExtensionsGrid.SelectedItems.Cast<ExtensionRow>().ToList();
        if (selected.Count == 0)
        {
            ShowMessage("Select one or more uninstalled extensions to reinstall.");
            return;
        }

        await RunBusyAsync("Reinstalling extensions...", async () =>
        {
            foreach (ExtensionRow row in selected.Where(row => !row.IsInstalled))
            {
                string? packagePath = await GetReinstallPackageAsync(row);
                if (packagePath is null)
                {
                    ShowMessage($"{row.DisplayName}: no cached VSIX or downloadable source is available.");
                    continue;
                }

                OperationResult result = await Task.Run(() => _installer.InstallLocalAsset(row.Instance, packagePath));
                if (result.Success)
                {
                    ExtensionAsset asset = _assetResolver.Resolve(packagePath, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
                    string? installedVersionOverride = row.AvailableUpdate?.Version ?? row.LatestRelease?.Version;
                    await SaveRecordAsync(row.Instance, asset.Manifest, row.UpdateSource, packagePath, isInstalled: true, installedVersionOverride: installedVersionOverride);
                }
                else
                {
                    ShowMessage($"{row.DisplayName}: {result.Message}");
                }
            }

            await LoadExtensionsAsync(checkUpdates: true);
        });
    }

    private async void SetSource_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        if (ExtensionsGrid.SelectedItem is not ExtensionRow row)
        {
            ShowMessage("Select an extension to set its update source.");
            return;
        }

        SourceDialog dialog = new(row.UpdateSource?.Uri ?? row.MoreInfo ?? string.Empty)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        UpdateSource source = new(dialog.SelectedSourceType, dialog.SourceUri);
        await _sourceStore.SetAsync(row.Manifest.Id, source);
        await SaveRecordAsync(row.Instance, row.Manifest, source, row.CachedVsixPath, row.IsInstalled, row.InstalledVersionOverride);
        await LoadExtensionsAsync(checkUpdates: true);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        if (ExtensionsGrid.SelectedItem is not ExtensionRow row || row.InstalledExtension is null)
        {
            ShowMessage("Select an installed extension to uninstall.");
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            this,
            $"Uninstall {row.DisplayName}? SSMS's VSIXInstaller will handle the uninstall.",
            "Uninstall extension",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Uninstalling extension...", async () =>
        {
            OperationResult result = await Task.Run(() => _installer.Uninstall(row.InstalledExtension));
            if (result.Success)
            {
                await SaveRecordAsync(row.Instance, row.Manifest, row.UpdateSource, row.CachedVsixPath, isInstalled: false, installedVersionOverride: null);
            }
            else
            {
                ShowMessage(result.Message);
            }

            await LoadExtensionsAsync(checkUpdates: true);
        });
    }

    private async void RemoveFromList_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        List<ExtensionRow> rows = ExtensionsGrid.SelectedItems.Cast<ExtensionRow>().Where(row => !row.IsInstalled).ToList();
        if (rows.Count == 0)
        {
            ShowMessage("Select one or more uninstalled extensions to remove from the list.");
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            this,
            "Remove selected uninstalled extension(s) from this list and delete cached VSIX packages?",
            "Remove from list",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        foreach (ExtensionRow row in rows)
        {
            _packageCache.RemoveCachedPackage(row.CachedVsixPath);
            await _managedStore.RemoveAsync(row.Instance.Id, row.Manifest.Id);
            await _sourceStore.RemoveAsync(row.Manifest.Id);
        }

        await LoadExtensionsAsync(checkUpdates: true);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentView(NavigationView.Settings);
    }

    private async void CheckApplicationUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RefreshApplicationUpdateStateAsync(interactive: true, promptToApply: true);
    }

    private async void UpdateAppButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshApplicationUpdateStateAsync(interactive: true, promptToApply: true);
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Right-click an extension row to update it, reinstall it, uninstall it, set its update source, or remove an uninstalled extension from the list.\n\nThe app caches VSIX packages that it installs or updates so those extensions can be reinstalled later. Extensions installed outside this app can be shown after uninstall, but they need a configured source before reinstall is reliable.\n\nUse File > Install VSIX/ZIP to install a local VSIX or ZIP containing one VSIX. Use View > Refresh to rescan SSMS, View > Update All to apply available installed-extension updates, and View > Settings to choose the SSMS instance or show Microsoft-published extensions.",
            "SSMS Extension Manager Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task RefreshApplicationUpdateStateAsync(bool interactive, bool promptToApply)
    {
        if (!_appUpdateService.IsConfigured)
        {
            _applicationUpdateResult = new AppUpdateCheckResult(AppUpdateCheckStatus.NotConfigured, null);
            UpdateApplicationUpdateButton();

            if (interactive)
            {
                ShowMessage("Application updates are not configured for this build.");
            }

            return;
        }

        AppUpdateCheckResult? result = null;

        if (interactive)
        {
            await RunBusyAsync("Checking for application updates...", async () =>
            {
                result = await _appUpdateService.CheckForUpdatesAsync();
            });
        }
        else
        {
            try
            {
                result = await _appUpdateService.CheckForUpdatesAsync();
            }
            catch
            {
                return;
            }
        }

        if (result is null)
        {
            return;
        }

        _applicationUpdateResult = result;
        UpdateApplicationUpdateButton();

        if (result.Status == AppUpdateCheckStatus.NotInstalled)
        {
            if (interactive)
            {
                ShowMessage("Application updates are only available when SSMS Extension Manager is installed from the Velopack setup package.");
            }

            return;
        }

        if (result.Status == AppUpdateCheckStatus.NoUpdateAvailable)
        {
            if (interactive)
            {
                ShowMessage("SSMS Extension Manager is up to date.");
            }

            return;
        }

        if (result.Update is not { } update)
        {
            return;
        }

        if (!promptToApply)
        {
            return;
        }

        bool pendingRestart = result.Status == AppUpdateCheckStatus.UpdatePendingRestart;
        string prompt = pendingRestart
            ? $"SSMS Extension Manager {update.Version} is ready to apply. Restart now?"
            : $"SSMS Extension Manager {update.Version} is available. Download, install, and restart now?";

        MessageBoxResult confirm = MessageBox.Show(
            this,
            prompt,
            "Application update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        if (!pendingRestart)
        {
            await RunBusyAsync("Downloading application update...", async () =>
            {
                await _appUpdateService.DownloadUpdateAsync(update, progress =>
                {
                    Dispatcher.Invoke(() => StatusText.Text = $"Downloading application update... {progress}%");
                });
            });
        }

        _appUpdateService.ApplyAndRestart(update);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _settingsStore.Save(BuildCurrentSettings());
        base.OnClosing(e);
    }

    private void ExtensionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionActionState();

    private void ExtensionLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Hyperlink { DataContext: ExtensionRow row } || row.GitHubUri is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = row.GitHubUri.ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
    }

    private void ConfigureRowActionsPopup()
    {
        ExtensionRow? row = ExtensionsGrid.SelectedItem as ExtensionRow;
        RowUpdateButton.Visibility = row?.IsInstalled == true ? Visibility.Visible : Visibility.Collapsed;
        RowReinstallButton.Visibility = row?.IsInstalled == false ? Visibility.Visible : Visibility.Collapsed;
        RowSetSourceButton.Visibility = row is not null ? Visibility.Visible : Visibility.Collapsed;
        RowUninstallButton.Visibility = row?.IsInstalled == true ? Visibility.Visible : Visibility.Collapsed;
        RowRemoveSeparator.Visibility = row?.IsInstalled == false ? Visibility.Visible : Visibility.Collapsed;
        RowRemoveButton.Visibility = row?.IsInstalled == false ? Visibility.Visible : Visibility.Collapsed;
        RowReinstallButton.IsEnabled = row?.CanReinstall == true;
    }

    private void CloseRowActionsPopup()
    {
        if (RowActionsPopup is not null)
        {
            RowActionsPopup.IsOpen = false;
        }
    }

    private void ExtensionsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindParent<Hyperlink>(source) is not null)
        {
            return;
        }

        if (FindParent<DataGridColumnHeader>(source) is not null)
        {
            return;
        }

        DataGridRow? row = FindParent<DataGridRow>(source);
        if (row is null)
        {
            ExtensionsGrid.SelectedItems.Clear();
            e.Handled = true;
            return;
        }

        if (row.IsSelected)
        {
            row.IsSelected = false;
            e.Handled = true;
            UpdateSelectionActionState();
        }
    }

    private void ExtensionsGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindParent<DataGridColumnHeader>(source) is not null)
        {
            return;
        }

        DataGridRow? row = FindParent<DataGridRow>(source);
        if (row is null)
        {
            ExtensionsGrid.SelectedItems.Clear();
            CloseRowActionsPopup();
            e.Handled = true;
            return;
        }

        ExtensionsGrid.SelectedItems.Clear();
        row.IsSelected = true;
        ConfigureRowActionsPopup();
        _pendingRowActionsPoint = e.GetPosition(ExtensionsGrid);
        e.Handled = true;
    }

    private void ExtensionsGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ExtensionsGrid.SelectedItem is not ExtensionRow)
        {
            return;
        }

        e.Handled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            RowActionsPopup.HorizontalOffset = _pendingRowActionsPoint.X;
            RowActionsPopup.VerticalOffset = _pendingRowActionsPoint.Y;
            RowActionsPopup.IsOpen = false;
            RowActionsPopup.IsOpen = true;
        });
    }

    private async Task RefreshAsync()
    {
        await RunBusyAsync("Detecting SSMS 22...", async () =>
        {
            IReadOnlyList<SsmsInstance> instances = await _instanceDetector.DetectAsync();
            string? selectedInstanceId = _preferredInstanceId ?? _selectedInstance?.Instance.Id;

            _instances.Clear();
            foreach (SsmsInstance instance in instances)
            {
                _instances.Add(new InstanceRow(instance));
            }

            _selectedInstance = _instances.FirstOrDefault(instance => string.Equals(instance.Instance.Id, selectedInstanceId, StringComparison.OrdinalIgnoreCase))
                ?? _instances.FirstOrDefault();
            _preferredInstanceId = _selectedInstance?.Instance.Id;
            InstanceListBox.ItemsSource = _instances;
            InstanceListBox.SelectedItem = _selectedInstance;
            InstanceDropDownButton.IsEnabled = _instances.Count > 0;
            UpdateInstanceDropDownText();

            if (_selectedInstance is null)
            {
                StatusText.Text = "No SSMS 22 installation was detected.";
                _allExtensions.Clear();
                _extensions.Clear();
                UpdateSelectionActionState();
            }
            else
            {
                await LoadExtensionsAsync(checkUpdates: true);
            }
        });
    }

    private async Task LoadExtensionsAsync(bool checkUpdates = false)
    {
        if (_selectedInstance is not { } instance)
        {
            return;
        }

        await RunBusyAsync(checkUpdates ? "Scanning extensions and checking updates..." : "Scanning extensions...", async () =>
        {
            IReadOnlyList<InstalledExtension> scanned = await _scanner.ScanAsync([instance.Instance]);
            IReadOnlyList<ManagedExtensionRecord> records = await _managedStore.LoadAsync();
            Dictionary<string, ManagedExtensionRecord> recordsById = records
                .Where(record => string.Equals(record.SsmsInstanceId, instance.Instance.Id, StringComparison.OrdinalIgnoreCase))
                .GroupBy(record => record.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(record => record.LastSeenAt).First(), StringComparer.OrdinalIgnoreCase);

            List<ExtensionRow> rows = [];
            HashSet<string> installedIds = new(StringComparer.OrdinalIgnoreCase);

            foreach (InstalledExtension extension in scanned)
            {
                recordsById.TryGetValue(extension.Manifest.Id, out ManagedExtensionRecord? record);
                UpdateSource? source = extension.UpdateSource ?? record?.UpdateSource ?? InferUpdateSource(extension.Manifest);
                InstalledExtension current = extension with
                {
                    UpdateSource = source,
                    InstalledVersionOverride = record?.InstalledVersionOverride
                };
                AvailableUpdate? latest = checkUpdates && source is { Type: UpdateSourceType.GitHubRelease }
                    ? await _updateChecker.FindLatestMatchingAssetAsync(extension.Manifest, source)
                    : null;
                string? installedVersionOverride = current.InstalledVersionOverride
                    ?? InferInstalledVersionOverride(record, latest, current.Manifest.Version);
                if (installedVersionOverride is not null && !string.Equals(installedVersionOverride, current.InstalledVersionOverride, StringComparison.OrdinalIgnoreCase))
                {
                    current = current with { InstalledVersionOverride = installedVersionOverride };
                }

                AvailableUpdate? update = latest is not null && VersionComparer.IsNewer(latest.Version, current.CurrentVersion)
                    ? latest
                    : null;

                rows.Add(new ExtensionRow(instance.Instance, current with { AvailableUpdate = update }, record, update, latest));
                installedIds.Add(extension.Manifest.Id);
                if (source is not null && extension.UpdateSource is null && record?.UpdateSource is null)
                {
                    await _sourceStore.SetAsync(extension.Manifest.Id, source);
                }
                await SaveRecordAsync(instance.Instance, extension.Manifest, source, record?.CachedVsixPath, isInstalled: true, current.InstalledVersionOverride);
            }

            foreach (ManagedExtensionRecord record in recordsById.Values.Where(record => !record.IsInstalled && !installedIds.Contains(record.Manifest.Id)))
            {
                UpdateSource? source = record.UpdateSource ?? InferUpdateSource(record.Manifest);
                AvailableUpdate? latest = checkUpdates && source is { Type: UpdateSourceType.GitHubRelease }
                    ? await _updateChecker.FindLatestMatchingAssetAsync(record.Manifest, source)
                    : null;

                ManagedExtensionRecord effectiveRecord = source == record.UpdateSource
                    ? record
                    : record with { UpdateSource = source };
                rows.Add(new ExtensionRow(instance.Instance, null, effectiveRecord, latest, latest));
                if (source is not null && record.UpdateSource is null)
                {
                    await _sourceStore.SetAsync(record.Manifest.Id, source);
                    await SaveRecordAsync(record.Manifest, instance.Instance, source, record.CachedVsixPath, isInstalled: false, record.InstalledVersionOverride);
                }
            }

            _allExtensions.Clear();
            _allExtensions.AddRange(rows);
            ApplyExtensionFilter();
        });
    }

    private void ApplyExtensionFilter()
    {
        List<ExtensionRow> visibleRows = _allExtensions
            .Where(row => _showMicrosoftExtensions || !row.IsMicrosoftPublisher)
            .OrderBy(row => row.IsInstalled ? 0 : 1)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _extensions.Clear();
        foreach (ExtensionRow row in visibleRows)
        {
            _extensions.Add(row);
        }

        int hiddenCount = _allExtensions.Count - visibleRows.Count;
        string instanceText = _selectedInstance is null ? "No SSMS instance selected" : _selectedInstance.Display;
        StatusText.Text = hiddenCount == 0
            ? $"{instanceText}. {FormatCount(_extensions.Count, "Extension")} shown."
            : $"{instanceText}. {FormatCount(_extensions.Count, "Extension")} shown. {FormatCount(hiddenCount, "Microsoft extension")} hidden.";
        UpdateSelectionActionState();
    }

    private void UpdateSelectionActionState()
    {
        List<ExtensionRow> selected = ExtensionsGrid?.SelectedItems.Cast<ExtensionRow>().ToList() ?? [];
        if (SelectionUpdateMenuItem is null)
        {
            return;
        }

        bool hasInstalled = selected.Any(row => row.IsInstalled);
        bool hasUninstalled = selected.Any(row => !row.IsInstalled);
        SelectionUpdateMenuItem.IsEnabled = hasInstalled;
        SelectionUninstallMenuItem.IsEnabled = hasInstalled;
        SelectionSetSourceMenuItem.IsEnabled = selected.Count == 1;
        SelectionReinstallMenuItem.IsEnabled = hasUninstalled && selected.All(row => row.IsInstalled || row.CanReinstall);
        SelectionRemoveMenuItem.IsEnabled = hasUninstalled;
    }

    private async Task UpdateRowsAsync(IReadOnlyList<ExtensionRow> rows, bool reportNoUpdate)
    {
        await RunBusyAsync("Updating extensions...", async () =>
        {
            int updatedCount = 0;
            int skippedCount = 0;

            foreach (ExtensionRow row in rows.Where(row => row.IsInstalled))
            {
                AvailableUpdate? update = row.AvailableUpdate;
                if (update is null)
                {
                    skippedCount++;
                    continue;
                }

                string downloaded = await DownloadAsync(update.AssetUri);
                try
                {
                    ExtensionAsset asset = _assetResolver.Resolve(downloaded, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
                    string cachedVsix = _packageCache.CacheVsix(asset.FilePath, asset.Manifest);
                    OperationResult result = await Task.Run(() => _installer.UpdateInstalledExtension(row.InstalledExtension!, cachedVsix));
                    if (result.Success)
                    {
                        updatedCount++;
                        await SaveRecordAsync(row.Instance, asset.Manifest, row.UpdateSource, cachedVsix, isInstalled: true, update.Version);
                    }
                    else
                    {
                        ShowMessage($"{row.DisplayName}: {result.Message}");
                    }
                }
                finally
                {
                    TryDelete(downloaded);
                }
            }

            await LoadExtensionsAsync(checkUpdates: true);
            if (reportNoUpdate && updatedCount == 0 && skippedCount > 0)
            {
                ShowMessage(rows.Count == 1
                    ? "No update available."
                    : "No updates available for the selected extensions.");
            }
        });
    }

    private async Task<string?> GetReinstallPackageAsync(ExtensionRow row)
    {
        if (row.CachedVsixPath is { } cached && File.Exists(cached))
        {
            return cached;
        }

        if (row.UpdateSource is not { Type: UpdateSourceType.GitHubRelease } source)
        {
            return null;
        }

        AvailableUpdate? asset = row.AvailableUpdate ?? await _updateChecker.FindLatestMatchingAssetAsync(row.Manifest, source);
        if (asset is null)
        {
            return null;
        }

        string downloaded = await DownloadAsync(asset.AssetUri);
        try
        {
            ExtensionAsset resolved = _assetResolver.Resolve(downloaded, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
            if (!string.Equals(resolved.Manifest.Id, row.Manifest.Id, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return _packageCache.CacheVsix(resolved.FilePath, resolved.Manifest);
        }
        finally
        {
            TryDelete(downloaded);
        }
    }

    private async Task SaveRecordAsync(SsmsInstance instance, VsixManifest manifest, UpdateSource? source, string? cachedVsixPath, bool isInstalled, string? installedVersionOverride)
        => await SaveRecordAsync(manifest, instance, source, cachedVsixPath, isInstalled, installedVersionOverride);

    private async Task SaveRecordAsync(VsixManifest manifest, SsmsInstance instance, UpdateSource? source, string? cachedVsixPath, bool isInstalled, string? installedVersionOverride)
    {
        await _managedStore.UpsertAsync(new ManagedExtensionRecord(
            instance.Id,
            manifest,
            source,
            cachedVsixPath,
            isInstalled,
            DateTimeOffset.UtcNow,
            installedVersionOverride));
    }

    private static UpdateSource? InferUpdateSource(VsixManifest manifest)
    {
        if (!GitHubRepository.TryParse(manifest.MoreInfo ?? string.Empty, out GitHubRepository repository))
        {
            return null;
        }

        return new UpdateSource(UpdateSourceType.GitHubRelease, repository.ToString());
    }

    private static string? InferInstalledVersionOverride(ManagedExtensionRecord? record, AvailableUpdate? latest, string manifestVersion)
    {
        if (record?.InstalledVersionOverride is not null)
        {
            return record.InstalledVersionOverride;
        }

        if (record is not { IsInstalled: true, CachedVsixPath: { } cachedPath } || latest is null || !File.Exists(cachedPath))
        {
            return null;
        }

        if (!VersionComparer.IsNewer(latest.Version, manifestVersion))
        {
            return null;
        }

        DateTimeOffset cachedTimestamp = File.GetLastWriteTimeUtc(cachedPath);
        return cachedTimestamp >= latest.PublishedAt.UtcDateTime.AddMinutes(-1)
            ? latest.Version
            : null;
    }

    private async Task<string> DownloadAsync(Uri uri)
    {
        string targetRoot = Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "downloads");
        Directory.CreateDirectory(targetRoot);
        string targetPath = Path.Combine(targetRoot, $"{Guid.NewGuid():N}{Path.GetExtension(uri.LocalPath)}");

        await using Stream input = await _httpClient.GetStreamAsync(uri);
        await using FileStream output = File.Create(targetPath);
        await input.CopyToAsync(output);
        return targetPath;
    }

    private async Task RunBusyAsync(string status, Func<Task> action)
    {
        try
        {
            BusyProgress.Visibility = Visibility.Visible;
            StatusText.Text = status;
            IsEnabled = false;
            await action();
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
        finally
        {
            IsEnabled = true;
            BusyProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowMessage(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "SSMS Extension Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? current = child;
        while (current is not null)
        {
            if (current is T typed)
            {
                return typed;
            }

            current = current switch
            {
                Visual or Visual3D => VisualTreeHelper.GetParent(current),
                FrameworkContentElement frameworkContentElement => frameworkContentElement.Parent,
                ContentElement contentElement => ContentOperations.GetParent(contentElement),
                _ => null
            };
        }

        return null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Best effort cleanup only.
        }
    }

    private async Task SaveSettingsAsync()
    {
        await _settingsStore.SaveAsync(BuildCurrentSettings());
    }

    private void ManageNavButton_Click(object sender, RoutedEventArgs e) => SetCurrentView(NavigationView.Manage);

    private void BrowseNavButton_Click(object sender, RoutedEventArgs e) => SetCurrentView(NavigationView.Browse);

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => SetCurrentView(NavigationView.Settings);

    private void SetCurrentView(NavigationView view)
    {
        _currentView = view;
        ApplyNavigation();
    }

    private void ApplyNavigation()
    {
        if (ManageView is null)
        {
            return;
        }

        ManageView.Visibility = _currentView == NavigationView.Manage ? Visibility.Visible : Visibility.Collapsed;
        BrowseView.Visibility = _currentView == NavigationView.Browse ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = _currentView == NavigationView.Settings ? Visibility.Visible : Visibility.Collapsed;

        PageTitleText.Text = _currentView switch
        {
            NavigationView.Manage => "Manage",
            NavigationView.Browse => "Browse",
            NavigationView.Settings => "Settings",
            _ => "SSMS Extension Manager"
        };

        PageSubtitleText.Text = _currentView switch
        {
            NavigationView.Manage => "Right-click extensions in the table below to take actions such as update or uninstall.",
            NavigationView.Browse => "Third-party SSMS extensions are not officially supported by Microsoft. Install only extensions you trust.",
            NavigationView.Settings => "Choose the SSMS instance and application behavior here.",
            _ => string.Empty
        };

        PageSubtitleText.Visibility = string.IsNullOrEmpty(PageSubtitleText.Text) ? Visibility.Collapsed : Visibility.Visible;
        ManageNavButton.Tag = _currentView == NavigationView.Manage ? "Active" : null;
        BrowseNavButton.Tag = _currentView == NavigationView.Browse ? "Active" : null;
        SettingsNavButton.Tag = _currentView == NavigationView.Settings ? "Active" : null;
    }

    private void InstanceDropDown_Click(object sender, RoutedEventArgs e)
    {
        if (!InstanceDropDownButton.IsEnabled)
        {
            return;
        }

        InstanceDropDownPopup.IsOpen = !InstanceDropDownPopup.IsOpen;
    }

    private async void InstanceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateInstanceDropDownText();

        if (!IsLoaded)
        {
            return;
        }

        InstanceDropDownPopup.IsOpen = false;
        if (InstanceListBox.SelectedItem is not InstanceRow selected || ReferenceEquals(_selectedInstance, selected))
        {
            return;
        }

        _selectedInstance = selected;
        _preferredInstanceId = selected.Instance.Id;
        await SaveSettingsAsync();
        await LoadExtensionsAsync(checkUpdates: true);
    }

    private void UpdateInstanceDropDownText()
    {
        InstanceDropDownText.Text = _selectedInstance?.Display ?? "No SSMS 22 instance detected";
    }

    private async void SettingsControl_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _isInitializingSettingsControls)
        {
            return;
        }

        bool previousShowMicrosoftExtensions = _showMicrosoftExtensions;
        bool previousDarkTheme = _darkTheme;
        bool previousCheckForApplicationUpdates = _checkForApplicationUpdates;
        _showMicrosoftExtensions = ShowMicrosoftExtensionsCheckBox.IsChecked == true;
        _darkTheme = DarkThemeCheckBox.IsChecked == true;
        _checkForApplicationUpdates = CheckForAppUpdatesCheckBox.IsChecked == true;

        if (previousDarkTheme != _darkTheme)
        {
            ThemeManager.Apply(_darkTheme);
        }

        if (previousShowMicrosoftExtensions != _showMicrosoftExtensions)
        {
            ApplyExtensionFilter();
        }

        if (previousCheckForApplicationUpdates && !_checkForApplicationUpdates)
        {
            _applicationUpdateResult = null;
            UpdateApplicationUpdateButton();
        }

        await SaveSettingsAsync();

        if (!previousCheckForApplicationUpdates && _checkForApplicationUpdates)
        {
            await RefreshApplicationUpdateStateAsync(interactive: false, promptToApply: false);
        }
    }

    private void UpdateApplicationUpdateButton()
    {
        if (UpdateAppButton is null)
        {
            return;
        }

        bool showButton = _checkForApplicationUpdates
            && _applicationUpdateResult?.Status is AppUpdateCheckStatus.UpdateAvailable or AppUpdateCheckStatus.UpdatePendingRestart;
        UpdateAppButton.Visibility = showButton ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseInstanceDropDownIfNeeded(MouseButtonEventArgs e)
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

    private static string FormatCount(int count, string singular)
    {
        string label = count == 1 ? singular : $"{singular}s";
        return $"{count} {label}";
    }

    private AppSettings BuildCurrentSettings() => new(
        _preferredInstanceId,
        ShowMicrosoftExtensionsCheckBox?.IsChecked ?? _showMicrosoftExtensions,
        DarkThemeCheckBox?.IsChecked ?? _darkTheme,
        CaptureWindowPlacement(),
        CheckForAppUpdatesCheckBox?.IsChecked ?? _checkForApplicationUpdates);

    private WindowPlacementSettings CaptureWindowPlacement()
    {
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        return new WindowPlacementSettings(
            bounds.Left,
            bounds.Top,
            bounds.Width,
            bounds.Height,
            WindowState == WindowState.Maximized);
    }

    private void ApplyWindowPlacement(WindowPlacementSettings? placement)
    {
        if (placement is null)
        {
            return;
        }

        double width = Math.Max(MinWidth, placement.Width);
        double height = Math.Max(MinHeight, placement.Height);
        Rect virtualScreen = new(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        Rect requested = new(placement.Left, placement.Top, width, height);

        if (!requested.IntersectsWith(virtualScreen))
        {
            return;
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = width;
        Height = height;
        Left = Math.Min(Math.Max(requested.Left, virtualScreen.Left), virtualScreen.Right - Width);
        Top = Math.Min(Math.Max(requested.Top, virtualScreen.Top), virtualScreen.Bottom - Height);

        if (placement.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }
}

public sealed class InstanceRow(SsmsInstance instance)
{
    public SsmsInstance Instance { get; } = instance;

    public string Display => $"{Instance.DisplayName} ({Instance.Version ?? "unknown"})";
}

public sealed class ExtensionRow(SsmsInstance instance, InstalledExtension? installedExtension, ManagedExtensionRecord? record, AvailableUpdate? availableUpdate, AvailableUpdate? latestRelease)
{
    public SsmsInstance Instance { get; } = instance;

    public InstalledExtension? InstalledExtension { get; } = installedExtension;

    public ManagedExtensionRecord? Record { get; } = record;

    public AvailableUpdate? AvailableUpdate { get; } = availableUpdate;

    public AvailableUpdate? LatestRelease { get; } = latestRelease;

    public VsixManifest Manifest => InstalledExtension?.Manifest ?? Record!.Manifest;

    public bool IsInstalled => InstalledExtension is not null;

    public string DisplayName => Manifest.DisplayName;

    public string Status => IsInstalled
        ? "Installed"
        : CanReinstall ? "Not installed" : "Not installed (source needed)";

    public string Publisher => Manifest.Publisher;

    public string InstalledVersion => IsInstalled ? (InstalledExtension!.CurrentVersion) : "";

    public string LatestVersion => LatestRelease?.Version ?? "";

    public string UpdateSourceText => UpdateSource?.Uri ?? "Unknown";

    public string Scope => IsInstalled
        ? InstalledExtension!.IsPerUser ? "Per-user" : "Machine"
        : HasCachedPackage ? "Cached" : "Known";

    public bool IsMicrosoftPublisher =>
        Publisher.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
        || Publisher.Equals("Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
        || Publisher.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase);

    public bool HasCachedPackage => CachedVsixPath is { } cached && File.Exists(cached);

    public bool CanReinstall => !IsInstalled && (HasCachedPackage || UpdateSource is { Type: UpdateSourceType.GitHubRelease });

    public string? CachedVsixPath => Record?.CachedVsixPath;

    public string? MoreInfo => Manifest.MoreInfo;

    public UpdateSource? UpdateSource => InstalledExtension?.UpdateSource ?? Record?.UpdateSource;

    public string? InstalledVersionOverride => InstalledExtension?.InstalledVersionOverride ?? Record?.InstalledVersionOverride;

    public Uri? GitHubUri
    {
        get
        {
            if (UpdateSource is not { Type: UpdateSourceType.GitHubRelease } source)
            {
                return null;
            }

            return GitHubRepository.TryParse(source.Uri, out GitHubRepository repository)
                ? repository.RepositoryUri
                : null;
        }
    }

    public bool HasGitHubUri => GitHubUri is not null;
}
