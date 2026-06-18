namespace SsmsExtensionManager.Core.Services;

public static class AppPaths
{
    private const string AppDirectoryName = "SsmsExtensionManager";
    private const string SettingsFileName = "settings.json";
    private const string ManagedExtensionsFileName = "managed-extensions.json";
    private const string ExtensionSourcesFileName = "extension-sources.json";
    private const string PackageCacheDirectoryName = "PackageCache";
    private const string InstallStagingDirectoryName = ".ssms-extension-manager";
    private const string InstallStagingPackagesDirectoryName = "install-staging";
    private const string AssetsDirectoryName = "assets";
    private const string UpdatesDirectoryName = "updates";
    private const string DownloadsDirectoryName = "downloads";

    public static string LocalDataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        AppDirectoryName);

    public static string SettingsFilePath => Path.Combine(LocalDataRoot, SettingsFileName);

    public static string ManagedExtensionsFilePath => Path.Combine(LocalDataRoot, ManagedExtensionsFileName);

    public static string ExtensionSourcesFilePath => Path.Combine(LocalDataRoot, ExtensionSourcesFileName);

    public static string PackageCacheRoot => Path.Combine(LocalDataRoot, PackageCacheDirectoryName);

    public static string InstallStagingRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        InstallStagingDirectoryName,
        InstallStagingPackagesDirectoryName);

    public static string TempRoot => Path.Combine(Path.GetTempPath(), AppDirectoryName);

    public static string TempAssetsRoot => Path.Combine(TempRoot, AssetsDirectoryName);

    public static string TempUpdatesRoot => Path.Combine(TempRoot, UpdatesDirectoryName);

    public static string TempDownloadsRoot => Path.Combine(TempRoot, DownloadsDirectoryName);
}
