# SSMS Extension Manager

A standalone C# WPF app for managing third-party SSMS 22+ VSIX extensions.

## Current Scope

- Detects SSMS 22+ installations through `vswhere.exe`, with a default install-path fallback.
- Scans SSMS 22 per-machine and per-user extension roots for `extension.vsixmanifest`.
- Displays installed extension identity, publisher, installed version, latest version, scope, and update source.
- Stores user-supplied update sources under `%LocalAppData%\SsmsExtensionManager\extension-sources.json`.
- Supports GitHub repository releases as the update source.
- Supports installing a local `.vsix` or `.zip` containing one `.vsix`.
- Supports updating selected extensions or all detected updates.
- Supports uninstall through the SSMS `VSIXInstaller.exe /u:<VSIX id>` path.
- Keeps uninstalled managed extensions visible so they can be reinstalled later.
- Caches VSIX packages installed or updated through the app under `%LocalAppData%\SsmsExtensionManager\PackageCache`.
- Supports removing uninstalled extensions from the list and deleting their cached VSIX packages.
- Supports Velopack app self-updates from GitHub Releases when installed from the setup package.

## Important Caveat

Microsoft does not officially support third-party SSMS extensions. This app intentionally treats VSIX identity as the source of truth and verifies that downloaded update candidates match the installed VSIX identity before updating.

## Run

In VS Code, run the `Run app for testing` task.

```powershell
dotnet run --project .\SsmsExtensionManager.App\SsmsExtensionManager.App.csproj
```

## Test

```powershell
dotnet test .\SsmsExtensionManager.slnx
```

## Release

Releases are packaged with Velopack and hosted on GitHub Releases.

The release workflow stamps the app update source as the repository running the workflow:

```text
https://github.com/${{ github.repository }}
```

To create a release, run the `release` workflow in GitHub with a version such as `0.1.0`, or push a tag:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## Known Gaps

- ZIP releases containing multiple VSIX files currently fail with a clear error instead of presenting a chooser.
- Protected per-machine installs may require elevation from SSMS's VSIX installer.
- Extensions installed outside this app can remain visible after uninstall, but reinstall requires a cached VSIX from this app or a configured downloadable update source.
