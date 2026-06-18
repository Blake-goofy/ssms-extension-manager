using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
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
    private readonly GalleryFeedReader _galleryFeedReader = new();
    private readonly HttpClient _httpClient = new();
    private readonly GalleryIconLoader _galleryIconLoader;
    private readonly ObservableCollection<InstanceRow> _instances = [];
    private readonly ObservableCollection<ExtensionRow> _extensions = [];
    private readonly ObservableCollection<GalleryExtensionRow> _galleryExtensions = [];
    private readonly List<ExtensionRow> _allExtensions = [];
    private readonly List<GalleryExtensionRow> _allGalleryExtensions = [];
    private readonly Dictionary<string, GalleryExtension> _galleryCatalogById = new(StringComparer.OrdinalIgnoreCase);
    private readonly InstalledExtensionScanner _scanner;
    private readonly ExtensionAssetResolver _assetResolver;
    private readonly ExtensionInstaller _installer;
    private readonly GitHubReleaseUpdateChecker _updateChecker;
    private Point _pendingRowActionsPoint;
    private Point _pendingGalleryActionsPoint;
    private AppUpdateCheckResult? _applicationUpdateResult;
    private InstanceRow? _selectedInstance;
    private NavigationView _currentView = NavigationView.Manage;
    private bool _isInitializingSettingsControls;
    private string _settingsStatusText = string.Empty;
    private bool _checkForApplicationUpdates = true;
    private bool _showMicrosoftExtensions;
    private bool _darkTheme;
    private string? _ssmsLaunchExecutablePath;
    private string _ssmsLaunchArguments = string.Empty;
    private string _manageViewMode = AppSettings.ManageViewModeTiles;
    private string _browseViewMode = AppSettings.ManageViewModeTiles;
    private string? _preferredInstanceId;
    private bool _galleryLoaded;
    private bool _galleryCatalogLoaded;
    private bool _syncingManageSelection;
    private DateTime? _lastSettingsFileWriteTimeUtc;
    private CancellationTokenSource? _busyCancellationTokenSource;

    private static readonly Uri GalleryFeedUri = new("https://ssmsgallery.azurewebsites.net/feed/");
    private static readonly string DefaultSsmsLaunchExecutablePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Microsoft SQL Server Management Studio 22",
        "Release",
        "Common7",
        "IDE",
        "SSMS.exe");

    public MainWindow()
    {
        InitializeComponent();
        ThemeManager.RegisterWindow(this);
        _assetResolver = new ExtensionAssetResolver(_manifestReader);
        _scanner = new InstalledExtensionScanner(_manifestReader, _sourceStore);
        _installer = new ExtensionInstaller(_assetResolver);
        _updateChecker = new GitHubReleaseUpdateChecker(_httpClient, _assetResolver);
        _galleryIconLoader = new GalleryIconLoader(_httpClient);
        ExtensionsGrid.ItemsSource = _extensions;
        ManageTilesListBox.ItemsSource = _extensions;
        GalleryListBox.ItemsSource = _galleryExtensions;
        GalleryGrid.ItemsSource = _galleryExtensions;
        UpdateManageSearchPlaceholderVisibility();
        UpdateGallerySearchPlaceholderVisibility();
        ApplyManageViewMode();
        ApplyBrowseViewMode();
        UpdateSelectionActionState();
        Title = $"SSMS Extension Manager {AppBuildInfo.Version}";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppSettings settings = await _settingsStore.LoadAsync();
        _showMicrosoftExtensions = settings.ShowMicrosoftExtensions;
        _darkTheme = settings.DarkTheme;
        _checkForApplicationUpdates = settings.CheckForApplicationUpdates;
        _ssmsLaunchExecutablePath = settings.SsmsLaunchExecutablePath;
        _ssmsLaunchArguments = settings.SsmsLaunchArguments;
        _manageViewMode = NormalizeViewMode(settings.ManageViewMode);
        _browseViewMode = NormalizeViewMode(settings.BrowseViewMode);
        _preferredInstanceId = settings.SelectedSsmsInstanceId;
        UpdateLastSettingsFileWriteTime();
        ThemeManager.Apply(_darkTheme);
        ApplyManageViewMode();
        ApplyBrowseViewMode();
        _isInitializingSettingsControls = true;
        ShowMicrosoftExtensionsCheckBox.IsChecked = _showMicrosoftExtensions;
        DarkThemeCheckBox.IsChecked = _darkTheme;
        CheckForAppUpdatesCheckBox.IsChecked = _checkForApplicationUpdates;
        SsmsLaunchExecutablePathTextBox.Text = _ssmsLaunchExecutablePath ?? string.Empty;
        SsmsLaunchArgumentsTextBox.Text = _ssmsLaunchArguments;
        _isInitializingSettingsControls = false;
        UpdateSsmsLaunchArgumentsPlaceholderVisibility();
        ApplyWindowPlacement(settings.WindowPlacement);
        await RefreshAsync(disableWindow: false);
        ApplyNavigation();
        if (_checkForApplicationUpdates || _appUpdateService.IsTestMode)
        {
            await RefreshApplicationUpdateStateAsync(interactive: false, promptToApply: false);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_currentView == NavigationView.Browse)
        {
            await LoadGalleryAsync(force: true);
            return;
        }

        await RefreshAsync();
    }

    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        CloseRowActionsPopup();
        CloseGalleryActionsPopup();
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

        SetCurrentView(NavigationView.Manage);
        await Dispatcher.Yield(DispatcherPriority.Background);

        await RunBusyAsync("Installing extension...", async cancellationToken =>
        {
            if (!EnsureSsmsClosedForExtensionMutation("installation"))
            {
                return;
            }

            ExtensionAsset asset = _assetResolver.Resolve(dialog.FileName, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
            string cachedVsix = _packageCache.CacheVsix(asset.FilePath, asset.Manifest);
            OperationResult result = await Task.Run(() => _installer.InstallLocalAsset(instance.Instance, cachedVsix, cancellationToken), cancellationToken);
            if (result.Success)
            {
                await SaveRecordAsync(
                    instance.Instance,
                    asset.Manifest,
                    null,
                    cachedVsix,
                    isInstalled: true,
                    installedVersionOverride: null,
                    timestampKind: ManagedExtensionRecord.InstalledTimestampKind,
                    timestampAt: DateTimeOffset.UtcNow);
            }
            else
            {
                ShowMessage(result.Message);
            }

            await LoadExtensionsAfterMutationAsync();
        }, allowCancel: true);
    }

    private async void UpdateSelected_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        List<ExtensionRow> selected = GetSelectedExtensionRows();
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
        List<ExtensionRow> selected = GetSelectedExtensionRows();
        if (selected.Count == 0)
        {
            ShowMessage("Select one or more uninstalled extensions to reinstall.");
            return;
        }

        await ReinstallRowsAsync(selected);
    }

    private async Task ReinstallRowsAsync(IReadOnlyList<ExtensionRow> selected)
    {
        SetCurrentView(NavigationView.Manage);
        await Dispatcher.Yield(DispatcherPriority.Background);

        await RunBusyAsync("Reinstalling extensions...", async cancellationToken =>
        {
            if (!EnsureSsmsClosedForExtensionMutation("installation"))
            {
                return;
            }

            foreach (ExtensionRow row in selected.Where(row => !row.IsInstalled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? packagePath = await GetReinstallPackageAsync(row, cancellationToken);
                if (packagePath is null)
                {
                    ShowMessage($"{row.DisplayName}: no cached VSIX or downloadable source is available.");
                    continue;
                }

                OperationResult result = await Task.Run(() => _installer.InstallLocalAsset(row.Instance, packagePath, cancellationToken), cancellationToken);
                if (result.Success)
                {
                    ExtensionAsset asset = _assetResolver.Resolve(packagePath, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
                    string? installedVersionOverride = row.AvailableUpdate?.Version ?? row.LatestRelease?.Version;
                    await SaveRecordAsync(
                        row.Instance,
                        asset.Manifest,
                        row.UpdateSource,
                        packagePath,
                        isInstalled: true,
                        installedVersionOverride: installedVersionOverride,
                        timestampKind: ManagedExtensionRecord.InstalledTimestampKind,
                        timestampAt: DateTimeOffset.UtcNow);
                }
                else
                {
                    ShowMessage($"{row.DisplayName}: {result.Message}");
                }
            }

            await LoadExtensionsAfterMutationAsync(selected.Select(row => row.Manifest.Id).ToArray());
        }, allowCancel: true);
    }

    private async void SetSource_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        if (GetPrimarySelectedExtensionRow() is not { } row)
        {
            ShowMessage("Select an extension to edit its update source.");
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
        await SaveRecordAsync(
            row.Instance,
            row.Manifest,
            source,
            row.CachedVsixPath,
            row.IsInstalled,
            row.InstalledVersionOverride,
            timestampKind: row.TimestampKind,
            timestampAt: row.TimestampAt);
        await LoadExtensionsAfterMutationAsync([row.Manifest.Id]);
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        if (GetPrimarySelectedExtensionRow() is not { } row || row.InstalledExtension is null)
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

        SetCurrentView(NavigationView.Manage);
        await Dispatcher.Yield(DispatcherPriority.Background);

        await RunBusyAsync("Uninstalling extension...", async cancellationToken =>
        {
            if (!EnsureSsmsClosedForExtensionMutation("uninstall"))
            {
                return;
            }

            OperationResult result = await Task.Run(() => _installer.Uninstall(row.InstalledExtension, cancellationToken), cancellationToken);
            if (result.Success)
            {
                await SaveRecordAsync(
                    row.Instance,
                    row.Manifest,
                    row.UpdateSource,
                    row.CachedVsixPath,
                    isInstalled: false,
                    installedVersionOverride: null,
                    timestampKind: ManagedExtensionRecord.UninstalledTimestampKind,
                    timestampAt: DateTimeOffset.UtcNow);
            }
            else
            {
                ShowMessage(result.Message);
            }

            await LoadExtensionsAfterMutationAsync([row.Manifest.Id]);
        }, allowCancel: true);
    }

    private async void RemoveFromList_Click(object sender, RoutedEventArgs e)
    {
        CloseRowActionsPopup();
        List<ExtensionRow> rows = GetSelectedExtensionRows().Where(row => !row.IsInstalled).ToList();
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

        await LoadExtensionsAfterMutationAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentView(NavigationView.Settings);
    }

    private void LaunchSsms_Click(object sender, RoutedEventArgs e)
    {
        string? executablePath = EmptyToNull(_ssmsLaunchExecutablePath);
        if (executablePath is null)
        {
            ShowLaunchSettingsError("The SSMS executable path is not set. Open Settings to choose the SSMS.exe start location.");
            return;
        }

        if (!File.Exists(executablePath))
        {
            ShowLaunchSettingsError($"The configured SSMS executable was not found:\n\n{executablePath}\n\nOpen Settings to update the start location.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = _ssmsLaunchArguments,
                WorkingDirectory = Path.GetDirectoryName(executablePath) ?? string.Empty,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            ShowLaunchSettingsError($"Unable to launch SSMS. {ex.Message}");
        }
    }

    private async void EditSettingsJson_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveSettingsAsync();
            Process.Start(new ProcessStartInfo
            {
                FileName = _settingsStore.FilePath,
                UseShellExecute = true
            });

            _settingsStatusText = $"Opened {_settingsStore.FilePath}. Restart the app after editing to reload changes.";
            UpdateFooterText();
        }
        catch (Exception ex)
        {
            ShowMessage($"Unable to open the settings JSON file. {ex.Message}");
        }
    }

    private async void CheckApplicationUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RefreshApplicationUpdateStateAsync(interactive: true, promptToApply: true);
    }

    private async void UpdateAppButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshApplicationUpdateStateAsync(interactive: true, promptToApply: true, confirmBeforeApply: false);
    }

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Use extension cards or list rows to update, reinstall, uninstall, edit update sources, or remove an uninstalled extension from the list.\n\nThe app caches VSIX packages that it installs or updates so those extensions can be reinstalled later. Extensions installed outside this app can be shown after uninstall, but they need a configured source before reinstall is reliable.\n\nUse File > Install VSIX/ZIP to install a local VSIX or ZIP containing one VSIX. Update sources can be a GitHub repository or a direct downloadable .vsix/.zip link. Use View > Refresh to rescan SSMS, View > Update All to apply available installed-extension updates, and View > Settings to choose the SSMS instance or show Microsoft-published extensions.",
            "SSMS Extension Manager Help",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task RefreshApplicationUpdateStateAsync(bool interactive, bool promptToApply, bool confirmBeforeApply = true)
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

        if (result.Version is not { } updateVersion)
        {
            return;
        }

        if (!promptToApply)
        {
            return;
        }

        bool pendingRestart = result.Status == AppUpdateCheckStatus.UpdatePendingRestart;
        if (confirmBeforeApply)
        {
            string prompt = pendingRestart
                ? $"SSMS Extension Manager {updateVersion} is ready to apply. Restart now?"
                : $"SSMS Extension Manager {updateVersion} is available. Download, install, and restart now?";

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
        }

        if (result.IsTestUpdate)
        {
            ShowMessage("Test update selected. No update was downloaded or applied because this app was launched with a forced test update.");
            return;
        }

        if (result.Update is not { } update)
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
        if (!SettingsFileChangedSinceLastAppSave())
        {
            SaveSettings();
        }

        base.OnClosing(e);
    }

    private void ExtensionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingManageSelection)
        {
            SyncManageSelection(ExtensionsGrid.SelectedItems.Cast<ExtensionRow>());
        }

        UpdateSelectionActionState();
    }

    private void ManageTilesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncingManageSelection)
        {
            SyncManageSelection(ManageTilesListBox.SelectedItems.Cast<ExtensionRow>());
        }

        UpdateSelectionActionState();
    }

    private void ExtensionLink_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Hyperlink { DataContext: ExtensionRow row })
        {
            OpenExtensionPage(row);
        }
    }

    private void OpenExtensionPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ExtensionRow row })
        {
            OpenExtensionPage(row);
        }
    }

    private async void UpdateExtensionTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ExtensionRow row })
        {
            return;
        }

        SelectSingleExtensionRow(row);
        await UpdateRowsAsync([row], reportNoUpdate: true);
    }

    private async void ReinstallExtensionTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ExtensionRow row })
        {
            return;
        }

        SelectSingleExtensionRow(row);
        await ReinstallRowsAsync([row]);
    }

    private void OpenExtensionPage(ExtensionRow row)
    {
        if (row.OpenUri is null)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = row.OpenUri.ToString(),
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
        ExtensionRow? row = GetPrimarySelectedExtensionRow();
        RowUpdateButton.Visibility = row?.HasAvailableUpdate == true ? Visibility.Visible : Visibility.Collapsed;
        RowReinstallButton.Visibility = row?.IsInstalled == false ? Visibility.Visible : Visibility.Collapsed;
        RowSetSourceButton.Visibility = row is not null ? Visibility.Visible : Visibility.Collapsed;
        RowUninstallButton.Visibility = row?.IsInstalled == true ? Visibility.Visible : Visibility.Collapsed;
        RowRemoveSeparator.Visibility = row?.IsInstalled == false ? Visibility.Visible : Visibility.Collapsed;
        RowRemoveButton.Visibility = row?.IsInstalled == false ? Visibility.Visible : Visibility.Collapsed;
        bool isManageable = row?.IsManageable == true;
        RowUpdateButton.IsEnabled = row?.CanUpdate == true;
        RowReinstallButton.IsEnabled = row?.CanReinstall == true;
        RowSetSourceButton.IsEnabled = isManageable;
        RowUninstallButton.IsEnabled = row?.CanUninstall == true;
        RowRemoveButton.IsEnabled = row?.CanRemoveFromList == true;
    }

    private void CloseRowActionsPopup()
    {
        if (RowActionsPopup is not null)
        {
            RowActionsPopup.IsOpen = false;
        }
    }

    private void ConfigureGalleryActionsPopup(GalleryExtensionRow? row)
    {
        GalleryActionsPopup.DataContext = row;
        GalleryInstallButton.DataContext = row;
        GalleryOpenPageButton.DataContext = row;
        GalleryInstallButton.IsEnabled = row?.CanInstall == true;
        GalleryOpenPageButton.IsEnabled = row?.HasPageUri == true;
    }

    private void CloseGalleryActionsPopup()
    {
        if (GalleryActionsPopup is not null)
        {
            GalleryActionsPopup.IsOpen = false;
        }
    }

    private async void ManageTilesViewButton_Click(object sender, RoutedEventArgs e)
    {
        SetManageViewMode(AppSettings.ManageViewModeTiles);
        await SaveSettingsAsync();
    }

    private async void ManageListViewButton_Click(object sender, RoutedEventArgs e)
    {
        SetManageViewMode(AppSettings.ManageViewModeList);
        await SaveSettingsAsync();
    }

    private async void BrowseTilesViewButton_Click(object sender, RoutedEventArgs e)
    {
        SetBrowseViewMode(AppSettings.ManageViewModeTiles);
        await SaveSettingsAsync();
    }

    private async void BrowseListViewButton_Click(object sender, RoutedEventArgs e)
    {
        SetBrowseViewMode(AppSettings.ManageViewModeList);
        await SaveSettingsAsync();
    }

    private void GalleryListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        ListBoxItem? item = FindParent<ListBoxItem>(source);
        if (item?.DataContext is not GalleryExtensionRow row)
        {
            GalleryListBox.SelectedItem = null;
            CloseGalleryActionsPopup();
            e.Handled = true;
            return;
        }

        GalleryListBox.SelectedItem = row;
        ConfigureGalleryActionsPopup(row);
        _pendingGalleryActionsPoint = e.GetPosition(BrowseView);
        e.Handled = true;
    }

    private void GalleryListBox_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (GalleryListBox.SelectedItem is not GalleryExtensionRow)
        {
            return;
        }

        ShowGalleryActionsPopup();
        e.Handled = true;
    }

    private void GalleryGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
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
        if (row?.Item is not GalleryExtensionRow galleryRow)
        {
            GalleryGrid.SelectedItem = null;
            CloseGalleryActionsPopup();
            e.Handled = true;
            return;
        }

        GalleryGrid.SelectedItem = galleryRow;
        ConfigureGalleryActionsPopup(galleryRow);
        _pendingGalleryActionsPoint = e.GetPosition(BrowseView);
        e.Handled = true;
    }

    private void GalleryGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (GalleryGrid.SelectedItem is not GalleryExtensionRow)
        {
            return;
        }

        ShowGalleryActionsPopup();
        e.Handled = true;
    }

    private void ShowGalleryActionsPopup()
    {
        CloseRowActionsPopup();
        GalleryActionsPopup.HorizontalOffset = _pendingGalleryActionsPoint.X;
        GalleryActionsPopup.VerticalOffset = _pendingGalleryActionsPoint.Y;
        GalleryActionsPopup.IsOpen = false;
        GalleryActionsPopup.IsOpen = true;
    }

    private void ExtensionTileActions_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ExtensionRow row } button)
        {
            return;
        }

        SelectSingleExtensionRow(row);
        ConfigureRowActionsPopup();
        Point buttonPoint = button.TranslatePoint(new Point(0, button.ActualHeight + 2), ManageView);
        RowActionsPopup.HorizontalOffset = buttonPoint.X;
        RowActionsPopup.VerticalOffset = buttonPoint.Y;
        RowActionsPopup.IsOpen = false;
        RowActionsPopup.IsOpen = true;
        e.Handled = true;
    }

    private List<ExtensionRow> GetSelectedExtensionRows()
    {
        IEnumerable selected = _manageViewMode == AppSettings.ManageViewModeList
            ? ExtensionsGrid.SelectedItems
            : ManageTilesListBox.SelectedItems;
        return selected.Cast<ExtensionRow>().ToList();
    }

    private ExtensionRow? GetPrimarySelectedExtensionRow()
        => _manageViewMode == AppSettings.ManageViewModeList
            ? ExtensionsGrid.SelectedItem as ExtensionRow
            : ManageTilesListBox.SelectedItem as ExtensionRow;

    private void SelectSingleExtensionRow(ExtensionRow row)
    {
        _syncingManageSelection = true;
        try
        {
            ExtensionsGrid.SelectedItems.Clear();
            ManageTilesListBox.SelectedItems.Clear();
            ExtensionsGrid.SelectedItem = row;
            ManageTilesListBox.SelectedItem = row;
        }
        finally
        {
            _syncingManageSelection = false;
        }

        UpdateSelectionActionState();
    }

    private void SyncManageSelection(IEnumerable<ExtensionRow> selectedRows)
    {
        List<ExtensionRow> rows = selectedRows.ToList();
        _syncingManageSelection = true;
        try
        {
            ExtensionsGrid.SelectedItems.Clear();
            ManageTilesListBox.SelectedItems.Clear();
            foreach (ExtensionRow row in rows)
            {
                ExtensionsGrid.SelectedItems.Add(row);
                ManageTilesListBox.SelectedItems.Add(row);
            }
        }
        finally
        {
            _syncingManageSelection = false;
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
        _pendingRowActionsPoint = e.GetPosition(ManageView);
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

    private async Task RefreshAsync(bool disableWindow = true)
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

            if (ApplyDetectedSsmsLaunchExecutablePathDefault(instances))
            {
                await SaveSettingsAsync();
            }

            if (_selectedInstance is null)
            {
                _allExtensions.Clear();
                _extensions.Clear();
                UpdateSelectionActionState();
                UpdateFooterText();
            }
            else
            {
                await LoadExtensionsAsync(checkUpdates: true, disableWindow: disableWindow);
            }
        }, disableWindow: disableWindow);
    }

    private bool ApplyDetectedSsmsLaunchExecutablePathDefault(IReadOnlyList<SsmsInstance> instances)
    {
        if (!string.IsNullOrWhiteSpace(_ssmsLaunchExecutablePath))
        {
            return false;
        }

        string? detectedPath = DetectSsmsLaunchExecutablePath(instances);
        if (detectedPath is null)
        {
            return false;
        }

        _ssmsLaunchExecutablePath = detectedPath;
        _isInitializingSettingsControls = true;
        try
        {
            SsmsLaunchExecutablePathTextBox.Text = detectedPath;
        }
        finally
        {
            _isInitializingSettingsControls = false;
        }

        return true;
    }

    private string? DetectSsmsLaunchExecutablePath(IReadOnlyList<SsmsInstance> instances)
    {
        IEnumerable<SsmsInstance> orderedInstances = _selectedInstance is null
            ? instances
            : new[] { _selectedInstance.Instance }.Concat(instances.Where(instance => !string.Equals(instance.Id, _selectedInstance.Instance.Id, StringComparison.OrdinalIgnoreCase)));

        foreach (SsmsInstance instance in orderedInstances)
        {
            string candidate = Path.Combine(instance.InstallationPath, "Common7", "IDE", "SSMS.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return File.Exists(DefaultSsmsLaunchExecutablePath)
            ? DefaultSsmsLaunchExecutablePath
            : null;
    }

    private async Task LoadGalleryAsync(bool force)
    {
        if (_galleryLoaded && !force)
        {
            return;
        }

        await RunBusyAsync("Loading gallery...", async () =>
        {
            GalleryStatusText.Text = "Loading gallery...";
            IReadOnlyList<GalleryExtension> extensions = await LoadGalleryCatalogAsync(force);

            string? selectedId = (GalleryListBox.SelectedItem as GalleryExtensionRow)?.Id;
            _allGalleryExtensions.Clear();
            _allGalleryExtensions.AddRange(extensions.Select(extension => new GalleryExtensionRow(extension, IsGalleryExtensionInstalled(extension))));
            await LoadGalleryIconsAsync(_allGalleryExtensions);
            _galleryLoaded = true;
            ApplyGalleryFilter(selectedId);
        }, disableWindow: false);
    }

    private async Task<IReadOnlyList<GalleryExtension>> LoadGalleryCatalogAsync(bool force)
    {
        if (_galleryCatalogLoaded && !force)
        {
            return _galleryCatalogById.Values.ToList();
        }

        await using Stream stream = await _httpClient.GetStreamAsync(GalleryFeedUri);
        IReadOnlyList<GalleryExtension> extensions = _galleryFeedReader.Read(stream);

        _galleryCatalogById.Clear();
        foreach (GalleryExtension extension in extensions)
        {
            _galleryCatalogById[extension.Id] = extension;
        }

        _galleryCatalogLoaded = true;
        return _galleryCatalogById.Values.ToList();
    }

    private async Task LoadGalleryIconsAsync(IEnumerable<GalleryExtensionRow> rows)
    {
        foreach (GalleryExtensionRow row in rows)
        {
            row.SetIconSource(await _galleryIconLoader.LoadAsync(row.IconUri));
        }
    }

    private async Task LoadExtensionIconsAsync(IEnumerable<ExtensionRow> rows)
    {
        foreach (ExtensionRow row in rows)
        {
            row.SetIconSource(await _galleryIconLoader.LoadAsync(row.IconUri));
        }
    }

    private void ApplyGalleryFilter(string? preferredSelectedId = null)
    {
        if (GallerySearchTextBox is null)
        {
            return;
        }

        string query = GallerySearchTextBox.Text.Trim();
        List<GalleryExtensionRow> rows = _allGalleryExtensions
            .Where(row => !row.IsInstalled)
            .Where(row => string.IsNullOrWhiteSpace(query)
                || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.AuthorText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Summary.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        _galleryExtensions.Clear();
        foreach (GalleryExtensionRow row in rows)
        {
            _galleryExtensions.Add(row);
        }

        GalleryStatusText.Text = _galleryLoaded
            ? $"{FormatCount(_galleryExtensions.Count, "Extension")} shown from SSMS Gallery."
            : string.Empty;

        GalleryExtensionRow? selected = !string.IsNullOrWhiteSpace(preferredSelectedId)
            ? _galleryExtensions.FirstOrDefault(row => string.Equals(row.Id, preferredSelectedId, StringComparison.OrdinalIgnoreCase))
            : GalleryListBox.SelectedItem as GalleryExtensionRow;

        if (selected is not null && _galleryExtensions.Contains(selected))
        {
            GalleryListBox.SelectedItem = selected;
        }
        else if (GalleryListBox.SelectedItem is not null && !_galleryExtensions.Contains(GalleryListBox.SelectedItem))
        {
            GalleryListBox.SelectedItem = null;
        }
    }

    private bool IsGalleryExtensionInstalled(GalleryExtension galleryExtension)
        => _allExtensions.Any(row => row.IsInstalled && GalleryExtensionMatcher.IsMatch(row.Manifest, galleryExtension));

    private void RefreshGalleryInstallStates()
    {
        string? selectedId = (GalleryListBox.SelectedItem as GalleryExtensionRow)?.Id
            ?? (GalleryGrid.SelectedItem as GalleryExtensionRow)?.Id;

        foreach (GalleryExtensionRow row in _allGalleryExtensions)
        {
            row.IsInstalled = IsGalleryExtensionInstalled(row.Extension);
        }

        GalleryListBox.Items.Refresh();
        GalleryGrid.Items.Refresh();
        ApplyGalleryFilter(selectedId);
    }

    private async Task InstallGalleryExtensionAsync(GalleryExtensionRow row)
    {
        if (_selectedInstance is not { } instance)
        {
            ShowMessage("No SSMS 22 instance is selected. Open View > Settings to choose an instance.");
            return;
        }

        if (row.IsInstalled)
        {
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            this,
            $"Install {row.DisplayName} from SSMS Gallery?",
            "Install extension",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        SetCurrentView(NavigationView.Manage);
        await Dispatcher.Yield(DispatcherPriority.Background);

        await RunBusyAsync($"Installing {row.DisplayName}...", async cancellationToken =>
        {
            string downloaded = await DownloadAsync(row.PackageUri, cancellationToken);
            string? installedManifestId = null;
            try
            {
                ExtensionAsset asset = _assetResolver.Resolve(downloaded, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
                if (!string.Equals(asset.Manifest.Id, row.Id, StringComparison.OrdinalIgnoreCase))
                {
                    ShowMessage($"Downloaded VSIX identity '{asset.Manifest.Id}' does not match gallery extension '{row.Id}'.");
                    return;
                }

                string cachedVsix = _packageCache.CacheVsix(asset.FilePath, asset.Manifest);
                if (!EnsureSsmsClosedForExtensionMutation("installation"))
                {
                    return;
                }

                OperationResult result = await Task.Run(() => _installer.InstallLocalAsset(instance.Instance, cachedVsix, cancellationToken), cancellationToken);
                if (!result.Success)
                {
                    ShowMessage(result.Message);
                    return;
                }

                UpdateSource source = new(SourceTypeFromPackageUri(row.PackageUri), row.PackageUri.ToString());
                await _sourceStore.SetAsync(asset.Manifest.Id, source);
                await SaveRecordAsync(
                    instance.Instance,
                    asset.Manifest,
                    source,
                    cachedVsix,
                    isInstalled: true,
                    installedVersionOverride: EmptyToNull(row.Version),
                    timestampKind: ManagedExtensionRecord.InstalledTimestampKind,
                    timestampAt: DateTimeOffset.UtcNow);
                installedManifestId = asset.Manifest.Id;
                GalleryStatusText.Text = result.Message;
            }
            finally
            {
                TryDelete(downloaded);
            }

            await LoadExtensionsAfterMutationAsync(installedManifestId is null ? null : [installedManifestId]);
            if (installedManifestId is not null)
            {
                SelectExtensionRow(installedManifestId);
                StatusText.Text = $"Installed {row.DisplayName}.";
            }

            RefreshGalleryInstallStates();
        }, allowCancel: true);
    }

    private static UpdateSourceType SourceTypeFromPackageUri(Uri uri)
    {
        string extension = Path.GetExtension(uri.LocalPath);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? UpdateSourceType.DirectZipUrl
            : UpdateSourceType.DirectVsixUrl;
    }

    private static bool IsDownloadableSource(UpdateSource? source)
        => source?.Type is UpdateSourceType.GitHubRelease or UpdateSourceType.DirectVsixUrl or UpdateSourceType.DirectZipUrl;

    private static AvailableUpdate? GetPreservedLatestRelease(ExtensionRow? previousRow, UpdateSource? source)
        => SameSource(previousRow?.UpdateSource, source)
            ? previousRow?.LatestRelease
            : null;

    private static bool SameSource(UpdateSource? left, UpdateSource? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Type == right.Type
            && string.Equals(left.Uri, right.Uri, StringComparison.OrdinalIgnoreCase);
    }

    private async Task LoadExtensionsAfterMutationAsync(IReadOnlyCollection<string>? refreshLatestIds = null)
        => await LoadExtensionsAsync(checkUpdates: false, refreshLatestIds);

    private async Task LoadExtensionsAsync(bool checkUpdates = false, IReadOnlyCollection<string>? refreshLatestIds = null, bool disableWindow = true)
    {
        if (_selectedInstance is not { } instance)
        {
            return;
        }

        await RunBusyAsync(checkUpdates ? "Scanning extensions and checking updates..." : "Scanning extensions...", async () =>
        {
            IReadOnlyList<GalleryExtension> galleryExtensions;
            try
            {
                galleryExtensions = await LoadGalleryCatalogAsync(force: false);
            }
            catch
            {
                galleryExtensions = [];
            }

            Dictionary<string, GalleryExtension> galleryById = galleryExtensions
                .GroupBy(extension => extension.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            HashSet<string> refreshLatestSet = refreshLatestIds is null
                ? new(StringComparer.OrdinalIgnoreCase)
                : new(refreshLatestIds, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ExtensionRow> previousRowsById = _allExtensions
                .GroupBy(row => row.Manifest.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
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
                DateTimeOffset lastSeenAt = DateTimeOffset.UtcNow;
                recordsById.TryGetValue(extension.Manifest.Id, out ManagedExtensionRecord? record);
                previousRowsById.TryGetValue(extension.Manifest.Id, out ExtensionRow? previousRow);
                GalleryExtension? galleryExtension = GalleryExtensionMatcher.MatchForManifest(extension.Manifest, galleryById);
                UpdateSource? extensionSource = GalleryExtensionMatcher.KeepCompatibleSource(extension.UpdateSource, galleryExtension);
                UpdateSource? recordSource = GalleryExtensionMatcher.KeepCompatibleSource(record?.UpdateSource, galleryExtension);
                UpdateSource? source = extensionSource ?? recordSource ?? InferUpdateSource(extension.Manifest) ?? InferUpdateSource(galleryExtension);
                bool removedStaleGallerySource = GalleryExtensionMatcher.IsGallerySource(extension.UpdateSource) && extensionSource is null;
                bool removedStaleRecordGallerySource = GalleryExtensionMatcher.IsGallerySource(record?.UpdateSource) && recordSource is null;
                string? normalizedInstalledVersionOverride = NormalizeInstalledVersionOverride(record?.InstalledVersionOverride, extension.Manifest.Version);
                InstalledExtension current = extension with
                {
                    UpdateSource = source,
                    InstalledVersionOverride = normalizedInstalledVersionOverride
                };
                bool shouldRefreshLatest = checkUpdates || refreshLatestSet.Contains(extension.Manifest.Id);
                AvailableUpdate? latest = shouldRefreshLatest && source is { } downloadableSource && IsDownloadableSource(downloadableSource)
                    ? await _updateChecker.FindLatestMatchingAssetAsync(extension.Manifest, downloadableSource)
                    : GetPreservedLatestRelease(previousRow, source);
                string? installedVersionOverride = current.InstalledVersionOverride
                    ?? InferInstalledVersionOverride(record, latest, current.Manifest.Version);
                if (installedVersionOverride is not null && !string.Equals(installedVersionOverride, current.InstalledVersionOverride, StringComparison.OrdinalIgnoreCase))
                {
                    current = current with { InstalledVersionOverride = installedVersionOverride };
                }

                AvailableUpdate? update = latest is not null && VersionComparer.IsNewer(latest.Version, current.CurrentVersion)
                    ? latest
                    : null;

                AvailableUpdate? effectiveLatest = latest ?? InferLatestFromGallery(galleryExtension);
                AvailableUpdate? effectiveUpdate = update;
                if (effectiveUpdate is null
                    && effectiveLatest is not null
                    && VersionComparer.IsNewer(effectiveLatest.Version, current.CurrentVersion))
                {
                    effectiveUpdate = effectiveLatest;
                }

                string timestampKind = NormalizeTimestampKind(record?.TimestampKind, isInstalled: true);
                DateTimeOffset timestampAt = record?.TimestampAt ?? lastSeenAt;
                rows.Add(new ExtensionRow(instance.Instance, current with { AvailableUpdate = effectiveUpdate }, record, effectiveUpdate, effectiveLatest, galleryExtension, lastSeenAt, timestampKind, timestampAt));
                installedIds.Add(extension.Manifest.Id);
                if (removedStaleGallerySource || removedStaleRecordGallerySource)
                {
                    await _sourceStore.RemoveAsync(extension.Manifest.Id);
                    if (source is not null)
                    {
                        await _sourceStore.SetAsync(extension.Manifest.Id, source);
                    }
                }
                else if (source is not null && extension.UpdateSource is null && record?.UpdateSource is null)
                {
                    await _sourceStore.SetAsync(extension.Manifest.Id, source);
                }
                await SaveRecordAsync(
                    instance.Instance,
                    extension.Manifest,
                    source,
                    record?.CachedVsixPath,
                    isInstalled: true,
                    current.InstalledVersionOverride,
                    lastSeenAt: lastSeenAt,
                    timestampKind: timestampKind,
                    timestampAt: timestampAt);
            }

            foreach (ManagedExtensionRecord record in recordsById.Values.Where(record => !record.IsInstalled && !installedIds.Contains(record.Manifest.Id)))
            {
                previousRowsById.TryGetValue(record.Manifest.Id, out ExtensionRow? previousRow);
                GalleryExtension? galleryExtension = GalleryExtensionMatcher.MatchForManifest(record.Manifest, galleryById);
                UpdateSource? recordSource = GalleryExtensionMatcher.KeepCompatibleSource(record.UpdateSource, galleryExtension);
                UpdateSource? source = recordSource ?? InferUpdateSource(record.Manifest) ?? InferUpdateSource(galleryExtension);
                bool removedStaleRecordGallerySource = GalleryExtensionMatcher.IsGallerySource(record.UpdateSource) && recordSource is null;
                bool shouldRefreshLatest = checkUpdates || refreshLatestSet.Contains(record.Manifest.Id);
                AvailableUpdate? latest = shouldRefreshLatest && source is { } downloadableSource && IsDownloadableSource(downloadableSource)
                    ? await _updateChecker.FindLatestMatchingAssetAsync(record.Manifest, downloadableSource)
                    : GetPreservedLatestRelease(previousRow, source);
                AvailableUpdate? effectiveLatest = latest ?? InferLatestFromGallery(galleryExtension);

                ManagedExtensionRecord effectiveRecord = source == record.UpdateSource
                    ? record
                    : record with { UpdateSource = source };
                string timestampKind = NormalizeTimestampKind(effectiveRecord.TimestampKind, isInstalled: false);
                DateTimeOffset timestampAt = effectiveRecord.TimestampAt ?? effectiveRecord.LastSeenAt;
                rows.Add(new ExtensionRow(instance.Instance, null, effectiveRecord, effectiveLatest, effectiveLatest, galleryExtension, effectiveRecord.LastSeenAt, timestampKind, timestampAt));
                if (removedStaleRecordGallerySource)
                {
                    await _sourceStore.RemoveAsync(record.Manifest.Id);
                }

                if (removedStaleRecordGallerySource || (source is not null && record.UpdateSource is null))
                {
                    if (source is not null)
                    {
                        await _sourceStore.SetAsync(record.Manifest.Id, source);
                    }

                    await SaveRecordAsync(
                        record.Manifest,
                        instance.Instance,
                        source,
                        record.CachedVsixPath,
                        isInstalled: false,
                        record.InstalledVersionOverride,
                        timestampKind: timestampKind,
                        timestampAt: timestampAt);
                }
            }

            _allExtensions.Clear();
            await LoadExtensionIconsAsync(rows);
            _allExtensions.AddRange(rows);
            ApplyExtensionFilter();
            if (_galleryLoaded)
            {
                RefreshGalleryInstallStates();
            }
        }, disableWindow: disableWindow);
    }

    private void ApplyExtensionFilter()
    {
        string query = ManageSearchTextBox?.Text.Trim() ?? string.Empty;
        List<ExtensionRow> selectedRows = GetSelectedExtensionRows();
        List<ExtensionRow> visibleRows = _allExtensions
            .Where(row => _showMicrosoftExtensions || !row.IsMicrosoftPublisher)
            .Where(row => string.IsNullOrWhiteSpace(query)
                || row.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Publisher.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.Status.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.VersionText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || row.UpdateSourceText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(row => row.IsInstalled ? 0 : 1)
            .ThenBy(row => row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _extensions.Clear();
        foreach (ExtensionRow row in visibleRows)
        {
            _extensions.Add(row);
        }

        SyncManageSelection(selectedRows.Where(_extensions.Contains));

        UpdateSelectionActionState();
        UpdateFooterText();
    }

    private void SelectExtensionRow(string manifestId)
    {
        ExtensionRow? row = _extensions.FirstOrDefault(row => string.Equals(row.Manifest.Id, manifestId, StringComparison.OrdinalIgnoreCase));
        if (row is null)
        {
            return;
        }

        SelectSingleExtensionRow(row);
        ExtensionsGrid.ScrollIntoView(row);
        ManageTilesListBox.ScrollIntoView(row);
        UpdateSelectionActionState();
    }

    private void UpdateSelectionActionState()
    {
        List<ExtensionRow> selected = GetSelectedExtensionRows();
        if (SelectionUpdateMenuItem is null)
        {
            return;
        }

        bool hasInstalled = selected.Any(row => row.IsInstalled);
        bool hasUninstalled = selected.Any(row => !row.IsInstalled);
        SelectionUpdateMenuItem.IsEnabled = selected.Any(row => row.CanUpdate);
        SelectionUninstallMenuItem.IsEnabled = selected.Any(row => row.CanUninstall);
        SelectionSetSourceMenuItem.IsEnabled = selected.Count == 1 && selected[0].IsManageable;
        SelectionReinstallMenuItem.IsEnabled = hasUninstalled && selected.Any(row => row.CanReinstall) && selected.All(row => row.IsInstalled || !row.IsManageable || row.CanReinstall);
        SelectionRemoveMenuItem.IsEnabled = hasUninstalled && selected.Any(row => row.CanRemoveFromList);
    }

    private async Task UpdateRowsAsync(IReadOnlyList<ExtensionRow> rows, bool reportNoUpdate)
    {
        string status = rows.Count == 1
            ? $"Updating {rows[0].DisplayName}..."
            : "Updating extensions...";

        SetCurrentView(NavigationView.Manage);
        await Dispatcher.Yield(DispatcherPriority.Background);

        await RunBusyAsync(status, async cancellationToken =>
        {
            int updatedCount = 0;
            int skippedCount = 0;

            if (!EnsureSsmsClosedForExtensionMutation("update"))
            {
                return;
            }

            foreach (ExtensionRow row in rows.Where(row => row.IsInstalled))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AvailableUpdate? update = row.AvailableUpdate;
                if (update is null)
                {
                    skippedCount++;
                    continue;
                }

                string downloaded = await DownloadAsync(update.AssetUri, cancellationToken);
                try
                {
                    ExtensionAsset asset = _assetResolver.Resolve(downloaded, Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "assets"));
                    string cachedVsix = _packageCache.CacheVsix(asset.FilePath, asset.Manifest);
                    OperationResult result = await Task.Run(() => _installer.UpdateInstalledExtension(row.InstalledExtension!, cachedVsix, cancellationToken), cancellationToken);
                    if (result.Success)
                    {
                        updatedCount++;
                        await SaveRecordAsync(
                            row.Instance,
                            asset.Manifest,
                            row.UpdateSource,
                            cachedVsix,
                            isInstalled: true,
                            update.Version,
                            timestampKind: ManagedExtensionRecord.UpdatedTimestampKind,
                            timestampAt: DateTimeOffset.UtcNow);
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

            await LoadExtensionsAfterMutationAsync(rows.Select(row => row.Manifest.Id).ToArray());
            if (reportNoUpdate && updatedCount == 0 && skippedCount > 0)
            {
                ShowMessage(rows.Count == 1
                    ? "No update available."
                    : "No updates available for the selected extensions.");
            }
        }, allowCancel: true);
    }

    private async Task<string?> GetReinstallPackageAsync(ExtensionRow row, CancellationToken cancellationToken)
    {
        if (row.CachedVsixPath is { } cached && File.Exists(cached))
        {
            return cached;
        }

        if (row.UpdateSource is not { } source || !IsDownloadableSource(source))
        {
            return null;
        }

        AvailableUpdate? asset = row.AvailableUpdate ?? await _updateChecker.FindLatestMatchingAssetAsync(row.Manifest, source, cancellationToken);
        if (asset is null)
        {
            return null;
        }

        string downloaded = await DownloadAsync(asset.AssetUri, cancellationToken);
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

    private async Task SaveRecordAsync(
        SsmsInstance instance,
        VsixManifest manifest,
        UpdateSource? source,
        string? cachedVsixPath,
        bool isInstalled,
        string? installedVersionOverride,
        DateTimeOffset? lastSeenAt = null,
        string? timestampKind = null,
        DateTimeOffset? timestampAt = null)
        => await SaveRecordAsync(manifest, instance, source, cachedVsixPath, isInstalled, installedVersionOverride, lastSeenAt, timestampKind, timestampAt);

    private async Task SaveRecordAsync(
        VsixManifest manifest,
        SsmsInstance instance,
        UpdateSource? source,
        string? cachedVsixPath,
        bool isInstalled,
        string? installedVersionOverride,
        DateTimeOffset? lastSeenAt = null,
        string? timestampKind = null,
        DateTimeOffset? timestampAt = null)
    {
        await _managedStore.UpsertAsync(new ManagedExtensionRecord(
            instance.Id,
            manifest,
            source,
            cachedVsixPath,
            isInstalled,
            lastSeenAt ?? DateTimeOffset.UtcNow,
            installedVersionOverride,
            timestampKind,
            timestampAt));
    }

    private static UpdateSource? InferUpdateSource(VsixManifest manifest)
    {
        if (!GitHubRepository.TryParse(manifest.MoreInfo ?? string.Empty, out GitHubRepository repository))
        {
            return null;
        }

        return new UpdateSource(UpdateSourceType.GitHubRelease, repository.ToString());
    }

    private static UpdateSource? InferUpdateSource(GalleryExtension? galleryExtension)
        => galleryExtension is null
            ? null
            : new UpdateSource(SourceTypeFromPackageUri(galleryExtension.PackageUri), galleryExtension.PackageUri.ToString());

    private static AvailableUpdate? InferLatestFromGallery(GalleryExtension? galleryExtension)
        => galleryExtension is null || string.IsNullOrWhiteSpace(galleryExtension.Version)
            ? null
            : new AvailableUpdate(
                galleryExtension.Version,
                galleryExtension.PackageUri,
                galleryExtension.PackageUri.ToString(),
                galleryExtension.PublishedAt ?? galleryExtension.UpdatedAt ?? DateTimeOffset.MinValue);

    private static string? InferInstalledVersionOverride(ManagedExtensionRecord? record, AvailableUpdate? latest, string manifestVersion)
    {
        if (record?.InstalledVersionOverride is not null)
        {
            return NormalizeInstalledVersionOverride(record.InstalledVersionOverride, manifestVersion);
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

    private static string? NormalizeInstalledVersionOverride(string? installedVersionOverride, string manifestVersion)
    {
        if (string.IsNullOrWhiteSpace(installedVersionOverride))
        {
            return null;
        }

        return VersionComparer.IsNewer(installedVersionOverride, manifestVersion)
            ? installedVersionOverride
            : null;
    }

    private static string NormalizeTimestampKind(string? timestampKind, bool isInstalled)
    {
        if (string.Equals(timestampKind, ManagedExtensionRecord.InstalledTimestampKind, StringComparison.OrdinalIgnoreCase))
        {
            return ManagedExtensionRecord.InstalledTimestampKind;
        }

        if (string.Equals(timestampKind, ManagedExtensionRecord.UpdatedTimestampKind, StringComparison.OrdinalIgnoreCase))
        {
            return ManagedExtensionRecord.UpdatedTimestampKind;
        }

        if (string.Equals(timestampKind, ManagedExtensionRecord.UninstalledTimestampKind, StringComparison.OrdinalIgnoreCase))
        {
            return ManagedExtensionRecord.UninstalledTimestampKind;
        }

        return isInstalled
            ? ManagedExtensionRecord.DetectedTimestampKind
            : ManagedExtensionRecord.UninstalledTimestampKind;
    }

    private async Task<string> DownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        string targetRoot = Path.Combine(Path.GetTempPath(), "SsmsExtensionManager", "downloads");
        Directory.CreateDirectory(targetRoot);
        string targetPath = Path.Combine(targetRoot, $"{Guid.NewGuid():N}{Path.GetExtension(uri.LocalPath)}");

        try
        {
            await using Stream input = await _httpClient.GetStreamAsync(uri, cancellationToken);
            await using FileStream output = File.Create(targetPath);
            await input.CopyToAsync(output, cancellationToken);
            return targetPath;
        }
        catch
        {
            TryDelete(targetPath);
            throw;
        }
    }

    private bool EnsureSsmsClosedForExtensionMutation(string operationName)
    {
        while (IsSsmsRunning())
        {
            StatusText.Text = "Close SSMS to continue.";
            MessageBoxResult result = MessageBox.Show(
                this,
                $"SQL Server Management Studio is running. Close SSMS before the {operationName} can proceed, then click OK.",
                "Close SSMS",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.OK)
            {
                StatusText.Text = "Operation canceled.";
                return false;
            }
        }

        return true;
    }

    private static bool IsSsmsRunning()
    {
        Process[] processes = [];
        try
        {
            processes = Process.GetProcessesByName("Ssms");
            foreach (Process process in processes)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        return true;
                    }
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private Task RunBusyAsync(string status, Func<Task> action, bool disableWindow = true)
        => RunBusyAsync(status, _ => action(), disableWindow);

    private async Task RunBusyAsync(string status, Func<CancellationToken, Task> action, bool disableWindow = true, bool allowCancel = false)
    {
        using CancellationTokenSource? cancellationTokenSource = allowCancel ? new CancellationTokenSource() : null;
        _busyCancellationTokenSource = cancellationTokenSource;

        try
        {
            FooterPanel.Visibility = Visibility.Visible;
            BusyProgress.Visibility = Visibility.Visible;
            CancelBusyButton.Visibility = allowCancel ? Visibility.Visible : Visibility.Collapsed;
            CancelBusyButton.IsEnabled = allowCancel;
            StatusText.Text = status;
            if (disableWindow)
            {
                SetMainInputEnabled(false);
            }

            await action(cancellationTokenSource?.Token ?? CancellationToken.None);
        }
        catch (OperationCanceledException) when (cancellationTokenSource?.IsCancellationRequested == true)
        {
            StatusText.Text = "Operation canceled.";
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
        finally
        {
            if (disableWindow)
            {
                SetMainInputEnabled(true);
            }

            _busyCancellationTokenSource = null;
            BusyProgress.Visibility = Visibility.Collapsed;
            CancelBusyButton.Visibility = Visibility.Collapsed;
            CancelBusyButton.IsEnabled = false;
            FooterPanel.Visibility = _currentView == NavigationView.Browse ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void SetMainInputEnabled(bool enabled)
    {
        MainMenu.IsEnabled = enabled;
        LaunchSsmsButton.IsEnabled = enabled;
        NavigationPanel.IsEnabled = enabled;
        PageHeaderPanel.IsEnabled = enabled;
        ViewHost.IsEnabled = enabled;
    }

    private void CancelBusy_Click(object sender, RoutedEventArgs e)
    {
        if (_busyCancellationTokenSource is not { IsCancellationRequested: false } cancellationTokenSource)
        {
            return;
        }

        CancelBusyButton.IsEnabled = false;
        StatusText.Text = "Canceling...";
        cancellationTokenSource.Cancel();
    }

    private void ShowMessage(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "SSMS Extension Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowLaunchSettingsError(string message)
    {
        StatusText.Text = message;

        Window dialog = new()
        {
            Title = "Launch SSMS",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            MinWidth = 420
        };

        Border root = new()
        {
            Padding = new Thickness(18)
        };
        root.SetResourceReference(Border.BackgroundProperty, "AppBackgroundBrush");
        root.SetResourceReference(TextElement.ForegroundProperty, "ControlForegroundBrush");

        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock messageText = new()
        {
            Text = message,
            MaxWidth = 520,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(messageText, 0);
        layout.Children.Add(messageText);

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        Button openSettingsButton = new()
        {
            Content = "Open Settings",
            IsDefault = true,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        openSettingsButton.Click += (_, _) => dialog.DialogResult = true;

        Button closeButton = new()
        {
            Content = "Close",
            IsCancel = true,
            Padding = new Thickness(12, 6, 12, 6)
        };
        closeButton.Click += (_, _) => dialog.DialogResult = false;

        buttons.Children.Add(openSettingsButton);
        buttons.Children.Add(closeButton);
        Grid.SetRow(buttons, 1);
        layout.Children.Add(buttons);
        root.Child = layout;
        dialog.Content = root;

        if (dialog.ShowDialog() == true)
        {
            SetCurrentView(NavigationView.Settings);
            SsmsLaunchExecutablePathTextBox.Focus();
            SsmsLaunchExecutablePathTextBox.SelectAll();
        }
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
        UpdateLastSettingsFileWriteTime();
    }

    private void SaveSettings()
    {
        _settingsStore.Save(BuildCurrentSettings());
        UpdateLastSettingsFileWriteTime();
    }

    private bool SettingsFileChangedSinceLastAppSave()
    {
        if (_lastSettingsFileWriteTimeUtc is null)
        {
            return false;
        }

        try
        {
            return !File.Exists(_settingsStore.FilePath)
                || File.GetLastWriteTimeUtc(_settingsStore.FilePath) != _lastSettingsFileWriteTimeUtc.Value;
        }
        catch
        {
            return false;
        }
    }

    private void UpdateLastSettingsFileWriteTime()
    {
        try
        {
            _lastSettingsFileWriteTimeUtc = File.Exists(_settingsStore.FilePath)
                ? File.GetLastWriteTimeUtc(_settingsStore.FilePath)
                : null;
        }
        catch
        {
            _lastSettingsFileWriteTimeUtc = null;
        }
    }

    private void ManageNavButton_Click(object sender, RoutedEventArgs e) => SetCurrentView(NavigationView.Manage);

    private async void BrowseNavButton_Click(object sender, RoutedEventArgs e)
    {
        SetCurrentView(NavigationView.Browse);
        await LoadGalleryAsync(force: false);
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => SetCurrentView(NavigationView.Settings);

    private void GallerySearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateGallerySearchPlaceholderVisibility();
        ApplyGalleryFilter();
    }

    private void ManageSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateManageSearchPlaceholderVisibility();
        ApplyExtensionFilter();
    }

    private async void InstallGalleryExtension_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: GalleryExtensionRow row })
        {
            CloseGalleryActionsPopup();
            await InstallGalleryExtensionAsync(row);
        }
    }

    private void GalleryIcon_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is Image image)
        {
            image.Visibility = Visibility.Collapsed;
        }
    }

    private void OpenGalleryPage_Click(object sender, RoutedEventArgs e)
    {
        CloseGalleryActionsPopup();
        if (sender is not Button { DataContext: GalleryExtensionRow { PageUri: { } pageUri } })
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = pageUri.ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
    }

    private void SetCurrentView(NavigationView view)
    {
        _currentView = view;
        ApplyNavigation();
    }

    private void SetManageViewMode(string mode)
    {
        _manageViewMode = NormalizeViewMode(mode);
        ApplyManageViewMode();
    }

    private void SetBrowseViewMode(string mode)
    {
        _browseViewMode = NormalizeViewMode(mode);
        ApplyBrowseViewMode();
    }

    private void ApplyManageViewMode()
    {
        if (ManageTilesListBox is null)
        {
            return;
        }

        bool tileView = _manageViewMode == AppSettings.ManageViewModeTiles;
        ManageTilesListBox.Visibility = tileView ? Visibility.Visible : Visibility.Collapsed;
        ExtensionsGrid.Visibility = tileView ? Visibility.Collapsed : Visibility.Visible;
        ManageTilesViewButton.Tag = tileView ? "Active" : null;
        ManageListViewButton.Tag = tileView ? null : "Active";
        UpdateSelectionActionState();
    }

    private void ApplyBrowseViewMode()
    {
        if (GalleryListBox is null)
        {
            return;
        }

        bool tileView = _browseViewMode == AppSettings.ManageViewModeTiles;
        GalleryListBox.Visibility = tileView ? Visibility.Visible : Visibility.Collapsed;
        GalleryGrid.Visibility = tileView ? Visibility.Collapsed : Visibility.Visible;
        BrowseTilesViewButton.Tag = tileView ? "Active" : null;
        BrowseListViewButton.Tag = tileView ? null : "Active";
    }

    private static string NormalizeViewMode(string? mode)
        => string.Equals(mode, AppSettings.ManageViewModeList, StringComparison.OrdinalIgnoreCase)
            ? AppSettings.ManageViewModeList
            : AppSettings.ManageViewModeTiles;

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
            NavigationView.Manage => "Use the extension cards or list below to update, uninstall, set sources, or open extension pages.",
            NavigationView.Browse => "Third-party SSMS extensions are not officially supported by Microsoft. Install only extensions you trust.",
            NavigationView.Settings => "Choose the SSMS instance and application behavior here.",
            _ => string.Empty
        };

        PageSubtitleText.Visibility = string.IsNullOrEmpty(PageSubtitleText.Text) ? Visibility.Collapsed : Visibility.Visible;
        ManageNavButton.Tag = _currentView == NavigationView.Manage ? "Active" : null;
        BrowseNavButton.Tag = _currentView == NavigationView.Browse ? "Active" : null;
        SettingsNavButton.Tag = _currentView == NavigationView.Settings ? "Active" : null;
        FooterPanel.Visibility = _currentView == NavigationView.Browse ? Visibility.Collapsed : Visibility.Visible;
        UpdateFooterText();
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

        _settingsStatusText = sender switch
        {
            CheckBox checkBox when checkBox == ShowMicrosoftExtensionsCheckBox
                => $"Show extensions published by Microsoft {EnabledDisabled(_showMicrosoftExtensions)}.",
            CheckBox checkBox when checkBox == DarkThemeCheckBox
                => $"Dark theme {EnabledDisabled(_darkTheme)}.",
            CheckBox checkBox when checkBox == CheckForAppUpdatesCheckBox
                => $"Check for application updates on startup {EnabledDisabled(_checkForApplicationUpdates)}.",
            _ => string.Empty
        };
        UpdateFooterText();
    }

    private async void SsmsLaunchSettings_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSsmsLaunchArgumentsPlaceholderVisibility();

        if (!IsLoaded || _isInitializingSettingsControls)
        {
            return;
        }

        _ssmsLaunchExecutablePath = EmptyToNull(SsmsLaunchExecutablePathTextBox.Text);
        _ssmsLaunchArguments = SsmsLaunchArgumentsTextBox.Text;
        await SaveSettingsAsync();

        _settingsStatusText = sender switch
        {
            TextBox textBox when textBox == SsmsLaunchExecutablePathTextBox => "SSMS launch executable path updated.",
            TextBox textBox when textBox == SsmsLaunchArgumentsTextBox => "SSMS launch arguments updated.",
            _ => string.Empty
        };
        UpdateFooterText();
    }

    private async void BrowseSsmsLaunchExecutable_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "SSMS executable (SSMS.exe)|SSMS.exe|Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = Path.GetFileName(_ssmsLaunchExecutablePath) ?? "SSMS.exe",
            Title = "Choose SSMS executable"
        };

        string? currentDirectory = Path.GetDirectoryName(_ssmsLaunchExecutablePath);
        if (!string.IsNullOrWhiteSpace(currentDirectory) && Directory.Exists(currentDirectory))
        {
            dialog.InitialDirectory = currentDirectory;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _ssmsLaunchExecutablePath = dialog.FileName;
        SsmsLaunchExecutablePathTextBox.Text = dialog.FileName;
        await SaveSettingsAsync();
        _settingsStatusText = "SSMS launch executable path updated.";
        UpdateFooterText();
    }

    private void UpdateSsmsLaunchArgumentsPlaceholderVisibility()
    {
        if (SsmsLaunchArgumentsPlaceholderText is null || SsmsLaunchArgumentsTextBox is null)
        {
            return;
        }

        SsmsLaunchArgumentsPlaceholderText.Visibility = string.IsNullOrWhiteSpace(SsmsLaunchArgumentsTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateApplicationUpdateButton()
    {
        if (UpdateAppButton is null)
        {
            return;
        }

        bool showButton = (_checkForApplicationUpdates || _applicationUpdateResult?.IsTestUpdate == true)
            && _applicationUpdateResult?.Status is AppUpdateCheckStatus.UpdateAvailable or AppUpdateCheckStatus.UpdatePendingRestart;
        UpdateAppButton.Visibility = showButton ? Visibility.Visible : Visibility.Collapsed;

        if (UpdateAppButtonText is not null)
        {
            bool pendingRestart = _applicationUpdateResult?.Status == AppUpdateCheckStatus.UpdatePendingRestart;
            UpdateAppButtonText.Text = pendingRestart ? "Restart to Update" : "Update";
            UpdateAppButton.ToolTip = pendingRestart
                ? "Application update ready to apply"
                : "Application update available";
        }
    }

    private void UpdateFooterText()
    {
        if (StatusText is null)
        {
            return;
        }

        StatusText.Text = _currentView switch
        {
            NavigationView.Manage => _selectedInstance is null
                ? "No SSMS 22 installation was detected."
                : $"{FormatCount(_extensions.Count, "Extension")} shown.",
            NavigationView.Settings => _settingsStatusText,
            _ => string.Empty
        };
    }

    private static string EnabledDisabled(bool enabled) => enabled ? "enabled" : "disabled";

    private static string? EmptyToNull(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
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
        CheckForAppUpdatesCheckBox?.IsChecked ?? _checkForApplicationUpdates,
        _manageViewMode,
        _browseViewMode,
        EmptyToNull(SsmsLaunchExecutablePathTextBox?.Text) ?? _ssmsLaunchExecutablePath,
        SsmsLaunchArgumentsTextBox?.Text ?? _ssmsLaunchArguments);

    private WindowPlacementSettings CaptureWindowPlacement()
    {
        Rect bounds = WindowState == WindowState.Normal
            ? new Rect(Left, Top, Width, Height)
            : RestoreBounds;

        return new WindowPlacementSettings(
            NormalizeWindowSetting(bounds.Left),
            NormalizeWindowSetting(bounds.Top),
            NormalizeWindowSetting(bounds.Width),
            NormalizeWindowSetting(bounds.Height),
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

    private void UpdateGallerySearchPlaceholderVisibility()
    {
        if (GallerySearchPlaceholderText is null || GallerySearchTextBox is null)
        {
            return;
        }

        GallerySearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(GallerySearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void UpdateManageSearchPlaceholderVisibility()
    {
        if (ManageSearchPlaceholderText is null || ManageSearchTextBox is null)
        {
            return;
        }

        ManageSearchPlaceholderText.Visibility = string.IsNullOrWhiteSpace(ManageSearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static double NormalizeWindowSetting(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class InstanceRow(SsmsInstance instance)
{
    public SsmsInstance Instance { get; } = instance;

    public string Display => $"{Instance.DisplayName} ({Instance.Version ?? "unknown"})";
}

public sealed class GalleryExtensionRow(GalleryExtension extension, bool isInstalled) : INotifyPropertyChanged
{
    private ImageSource? _iconSource;
    private bool _isInstalled = isInstalled;

    public event PropertyChangedEventHandler? PropertyChanged;

    public GalleryExtension Extension { get; } = extension;

    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled == value)
            {
                return;
            }

            _isInstalled = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(InstallButtonText));
        }
    }

    public string Id => Extension.Id;

    public string DisplayName => Extension.DisplayName;

    public string Summary => string.IsNullOrWhiteSpace(Extension.Summary)
        ? "No description provided."
        : Extension.Summary;

    public string AuthorText => string.IsNullOrWhiteSpace(Extension.Author)
        ? "Unknown publisher"
        : $"by {Extension.Author}";

    public string Version => Extension.Version;

    public string VersionText => string.IsNullOrWhiteSpace(Version)
        ? "Version unknown"
        : $"v{Version}";

    public Uri PackageUri => Extension.PackageUri;

    public Uri? PageUri => Extension.PageUri;

    public bool HasPageUri => PageUri is not null;

    public Uri? IconUri => Extension.IconUri;

    public ImageSource? IconSource => _iconSource;

    public bool CanInstall => !IsInstalled;

    public string InstallButtonText => IsInstalled ? "Installed" : "Install";

    public string Initials
    {
        get
        {
            string[] words = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }
    }

    public void SetIconSource(ImageSource? iconSource)
    {
        if (ReferenceEquals(_iconSource, iconSource))
        {
            return;
        }

        _iconSource = iconSource;
        OnPropertyChanged(nameof(IconSource));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ExtensionRow(
    SsmsInstance instance,
    InstalledExtension? installedExtension,
    ManagedExtensionRecord? record,
    AvailableUpdate? availableUpdate,
    AvailableUpdate? latestRelease,
    GalleryExtension? galleryExtension = null,
    DateTimeOffset? lastSeenAt = null,
    string? timestampKind = null,
    DateTimeOffset? timestampAt = null) : INotifyPropertyChanged
{
    private ImageSource? _iconSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public SsmsInstance Instance { get; } = instance;

    public InstalledExtension? InstalledExtension { get; } = installedExtension;

    public ManagedExtensionRecord? Record { get; } = record;

    public AvailableUpdate? AvailableUpdate { get; } = availableUpdate;

    public AvailableUpdate? LatestRelease { get; } = latestRelease;

    public GalleryExtension? GalleryExtension { get; } = galleryExtension;

    public VsixManifest Manifest => InstalledExtension?.Manifest ?? Record!.Manifest;

    public bool IsInstalled => InstalledExtension is not null;

    public string DisplayName => Manifest.DisplayName;

    public string Description => string.IsNullOrWhiteSpace(GalleryExtension?.Summary)
        ? string.IsNullOrWhiteSpace(Manifest.Description)
            ? "No description provided."
            : Manifest.Description!
        : GalleryExtension.Summary!;

    public string AuthorText => string.IsNullOrWhiteSpace(Publisher)
        ? "Unknown publisher"
        : $"by {Publisher}";

    public string Status => IsInstalled
        ? "Installed"
        : CanReinstall ? "Not installed" : "Not installed (source needed)";

    public string Publisher => Manifest.Publisher;

    public string InstalledVersion => IsInstalled ? (InstalledExtension!.CurrentVersion) : "";

    public string LatestVersion => LatestRelease?.Version ?? "";

    public string VersionText => IsInstalled
        ? string.IsNullOrWhiteSpace(InstalledVersion) ? "Version unknown" : $"v{InstalledVersion}"
        : ReinstallVersion is { } reinstallVersion ? $"v{reinstallVersion}" : "Version unknown";

    private string? ReinstallVersion => EmptyToNull(AvailableUpdate?.Version)
        ?? EmptyToNull(LatestRelease?.Version)
        ?? EmptyToNull(Record?.InstalledVersionOverride)
        ?? EmptyToNull(Manifest.Version);

    public string TimestampText => TimestampAt is { } timestampAt
        ? timestampAt.LocalDateTime.ToString("g")
        : "";

    public string TimestampTooltipText => TimestampAt is { } timestampAt
        ? $"{TimestampKind} at {timestampAt.LocalDateTime:g}"
        : $"{TimestampKind} at";

    public string TimestampKind { get; } = string.IsNullOrWhiteSpace(timestampKind)
        ? (installedExtension is null
            ? ManagedExtensionRecord.UninstalledTimestampKind
            : ManagedExtensionRecord.DetectedTimestampKind)
        : timestampKind!;

    public DateTimeOffset? TimestampAt { get; } = timestampAt ?? lastSeenAt ?? record?.TimestampAt ?? record?.LastSeenAt;

    public Uri? IconUri => GalleryExtension?.IconUri;

    public ImageSource? IconSource => _iconSource;

    public string UpdateSourceText => UpdateSource?.Uri ?? (IsMicrosoftPublisher ? "Microsoft" : "Unknown");

    public string Scope => IsInstalled
        ? InstalledExtension!.IsPerUser ? "Per-user" : "Machine"
        : HasCachedPackage ? "Cached" : "Known";

    public bool IsMicrosoftPublisher =>
        Publisher.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)
        || Publisher.Equals("Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
        || Publisher.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase);

    public bool IsManageable => !IsMicrosoftPublisher;

    public bool HasUpdateSource => UpdateSource is not null;

    public bool CanUpdate => IsManageable && IsInstalled && HasUpdateSource;

    public bool HasAvailableUpdate => AvailableUpdate is not null && CanUpdate;

    public bool CanUninstall => IsManageable && IsInstalled;

    public bool HasCachedPackage => CachedVsixPath is { } cached && File.Exists(cached);

    public bool CanReinstall => IsManageable && !IsInstalled && (HasCachedPackage || UpdateSource?.Type is UpdateSourceType.GitHubRelease or UpdateSourceType.DirectVsixUrl or UpdateSourceType.DirectZipUrl);

    public bool CanRemoveFromList => IsManageable && !IsInstalled;

    public string? CachedVsixPath => Record?.CachedVsixPath;

    public string? MoreInfo => Manifest.MoreInfo;

    public UpdateSource? UpdateSource => InstalledExtension?.UpdateSource ?? Record?.UpdateSource ?? InferredGalleryUpdateSource;

    public string? InstalledVersionOverride => InstalledExtension?.InstalledVersionOverride ?? Record?.InstalledVersionOverride;

    private UpdateSource? InferredGalleryUpdateSource => GalleryExtension is null
        ? null
        : new UpdateSource(GetSourceTypeFromPackageUri(GalleryExtension.PackageUri), GalleryExtension.PackageUri.ToString());

    public Uri? OpenUri
    {
        get
        {
            if (GalleryExtension?.PageUri is { } galleryOpenUri)
            {
                return galleryOpenUri;
            }

            if (TryGetGalleryPageUri(UpdateSource?.Uri, out Uri? resolvedGalleryPageUri))
            {
                return resolvedGalleryPageUri;
            }

            if (UpdateSource is { Type: UpdateSourceType.GitHubRelease } source
                && GitHubRepository.TryParse(source.Uri, out GitHubRepository repository))
            {
                return repository.RepositoryUri;
            }

            if (Uri.TryCreate(MoreInfo, UriKind.Absolute, out Uri? moreInfoUri))
            {
                return moreInfoUri;
            }

            return Uri.TryCreate(UpdateSource?.Uri, UriKind.Absolute, out Uri? sourceUri)
                ? sourceUri
                : null;
        }
    }

    public bool HasOpenUri => OpenUri is not null;

    public string Initials
    {
        get
        {
            string[] words = DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        }
    }

    public void SetIconSource(ImageSource? iconSource)
    {
        if (ReferenceEquals(_iconSource, iconSource))
        {
            return;
        }

        _iconSource = iconSource;
        OnPropertyChanged(nameof(IconSource));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static bool TryGetGalleryPageUri(string? sourceUri, out Uri? pageUri)
    {
        pageUri = null;
        if (!Uri.TryCreate(sourceUri, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "ssmsgallery.azurewebsites.net", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !string.Equals(segments[0], "extensions", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        pageUri = new Uri($"{uri.Scheme}://{uri.Authority}/extension/{Uri.EscapeDataString(segments[1])}");
        return true;
    }

    private static UpdateSourceType GetSourceTypeFromPackageUri(Uri uri)
    {
        string extension = Path.GetExtension(uri.LocalPath);
        return extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
            ? UpdateSourceType.DirectZipUrl
            : UpdateSourceType.DirectVsixUrl;
    }

    private static string? EmptyToNull(string? value)
    {
        value = value?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
