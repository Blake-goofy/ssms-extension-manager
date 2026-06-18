using Velopack;
using Velopack.Sources;

namespace SsmsExtensionManager.App;

public sealed class AppUpdateService
{
    private const string ForceUpdateEnvironmentVariable = "SSMS_EXTENSION_MANAGER_FORCE_APP_UPDATE";
    private const string TestUpdateVersionEnvironmentVariable = "SSMS_EXTENSION_MANAGER_TEST_APP_UPDATE_VERSION";

    public bool IsConfigured => IsForcedUpdateEnabled()
        || !string.IsNullOrWhiteSpace(AppBuildInfo.UpdateSourceUrl);

    public bool IsTestMode => IsForcedUpdateEnabled();

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (IsForcedUpdateEnabled())
        {
            return new AppUpdateCheckResult(
                AppUpdateCheckStatus.UpdateAvailable,
                null,
                IsTestUpdate: true,
                TestVersion: GetTestUpdateVersion());
        }

        if (!IsConfigured)
        {
            return new AppUpdateCheckResult(AppUpdateCheckStatus.NotConfigured, null);
        }

        UpdateManager manager = CreateManager();
        if (!manager.IsInstalled)
        {
            return new AppUpdateCheckResult(AppUpdateCheckStatus.NotInstalled, null);
        }

        if (manager.UpdatePendingRestart is { } pending)
        {
            return new AppUpdateCheckResult(
                AppUpdateCheckStatus.UpdatePendingRestart,
                new AppUpdate(pending, null, pending.Version.ToString()));
        }

        UpdateInfo? update = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        if (update is null)
        {
            return new AppUpdateCheckResult(AppUpdateCheckStatus.NoUpdateAvailable, null);
        }

        return new AppUpdateCheckResult(
            AppUpdateCheckStatus.UpdateAvailable,
            new AppUpdate(update.TargetFullRelease, update, update.TargetFullRelease.Version.ToString()));
    }

    public async Task DownloadUpdateAsync(AppUpdate update, Action<int> progress, CancellationToken cancellationToken = default)
    {
        if (update.UpdateInfo is null)
        {
            return;
        }

        UpdateManager manager = CreateManager();
        await manager.DownloadUpdatesAsync(update.UpdateInfo, progress, cancellationToken).ConfigureAwait(false);
    }

    public void ApplyAndRestart(AppUpdate update)
    {
        UpdateManager manager = CreateManager();
        manager.ApplyUpdatesAndRestart(update.TargetRelease, []);
    }

    private static UpdateManager CreateManager()
    {
        GithubSource source = new(AppBuildInfo.UpdateSourceUrl, accessToken: null, prerelease: false);
        return new UpdateManager(source);
    }

    private static bool IsForcedUpdateEnabled()
    {
        string? value = Environment.GetEnvironmentVariable(ForceUpdateEnvironmentVariable);
        return value is not null
            && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
            && !value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTestUpdateVersion()
    {
        string? version = Environment.GetEnvironmentVariable(TestUpdateVersionEnvironmentVariable);
        return string.IsNullOrWhiteSpace(version) ? "999.0.0-test" : version;
    }
}

public sealed record AppUpdate(VelopackAsset TargetRelease, UpdateInfo? UpdateInfo, string Version);

public sealed record AppUpdateCheckResult(
    AppUpdateCheckStatus Status,
    AppUpdate? Update,
    bool IsTestUpdate = false,
    string? TestVersion = null)
{
    public string? Version => Update?.Version ?? TestVersion;
}

public enum AppUpdateCheckStatus
{
    NotConfigured,
    NotInstalled,
    NoUpdateAvailable,
    UpdateAvailable,
    UpdatePendingRestart
}
