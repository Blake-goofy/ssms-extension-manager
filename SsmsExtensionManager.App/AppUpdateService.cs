using Velopack;
using Velopack.Sources;

namespace SsmsExtensionManager.App;

public sealed class AppUpdateService
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(AppBuildInfo.UpdateSourceUrl);

    public async Task<AppUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
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
}

public sealed record AppUpdate(VelopackAsset TargetRelease, UpdateInfo? UpdateInfo, string Version);

public sealed record AppUpdateCheckResult(AppUpdateCheckStatus Status, AppUpdate? Update);

public enum AppUpdateCheckStatus
{
    NotConfigured,
    NotInstalled,
    NoUpdateAvailable,
    UpdateAvailable,
    UpdatePendingRestart
}
