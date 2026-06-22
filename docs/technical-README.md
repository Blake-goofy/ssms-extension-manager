# Technical README

Developer-facing notes for running, testing, and releasing `ssms-extension-manager`.

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
