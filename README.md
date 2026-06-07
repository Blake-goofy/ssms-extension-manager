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

```powershell
dotnet run --project .\SsmsExtensionManager.App\SsmsExtensionManager.App.csproj
```

## Test

```powershell
dotnet test .\SsmsExtensionManager.slnx
```

## Release

Releases are packaged with Velopack and hosted on GitHub Releases. Local/dev builds default to `0.0.0`; Velopack packages must be `0.0.1` or greater, so the first installable release should be `v0.0.1`.

The release workflow stamps the app update source as the repository running the workflow:

```text
https://github.com/${{ github.repository }}
```

To create a release manually in GitHub, run the `release` workflow with a version such as `0.0.1`, or push a tag:

```powershell
git tag v0.0.1
git push origin v0.0.1
```

For local packaging, restore the local tools and pass the GitHub repository URL explicitly:

```powershell
dotnet tool restore
dotnet publish .\SsmsExtensionManager.App\SsmsExtensionManager.App.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\publish -p:Version=0.0.1 -p:AppUpdateSourceUrl=https://github.com/OWNER/REPO
dotnet tool run vpk pack --packId SsmsExtensionManager --packVersion 0.0.1 --packDir .\artifacts\publish --mainExe SsmsExtensionManager.App.exe --packTitle "SSMS Extension Manager" --outputDir .\artifacts\Releases --channel win --runtime win-x64
```

## Known Gaps

- ZIP releases containing multiple VSIX files currently fail with a clear error instead of presenting a chooser.
- Protected per-machine installs may require elevation from SSMS's VSIX installer.
- Extensions installed outside this app can remain visible after uninstall, but reinstall requires a cached VSIX from this app or a configured downloadable update source.
