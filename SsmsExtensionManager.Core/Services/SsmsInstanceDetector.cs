using System.Diagnostics;
using System.Text.Json;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class SsmsInstanceDetector
{
    private const string SsmsProductId = "Microsoft.VisualStudio.Product.SSMS";

    public async Task<IReadOnlyList<SsmsInstance>> DetectAsync(CancellationToken cancellationToken = default)
    {
        List<SsmsInstance> instances = [];

        foreach (VswhereInstance instance in await DetectWithVswhereAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(instance.ProductId, SsmsProductId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddInstance(instances, instance.InstanceId ?? instance.InstallationPath, instance.DisplayName ?? "SQL Server Management Studio", instance.InstallationVersion, instance.InstallationPath);
        }

        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Microsoft SQL Server Management Studio 22",
            "Release");

        if (Directory.Exists(defaultPath))
        {
            AddInstance(instances, "SSMS22.Default", "SQL Server Management Studio 22", ReadProductVersion(defaultPath), defaultPath);
        }

        return instances
            .GroupBy(instance => Path.GetFullPath(instance.InstallationPath), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(instance => instance.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddInstance(List<SsmsInstance> instances, string id, string displayName, string? version, string installationPath)
    {
        string normalizedInstallPath = Path.GetFullPath(installationPath);
        List<string> extensionRoots = [];

        string machineRoot = Path.Combine(normalizedInstallPath, "Common7", "IDE", "Extensions");
        if (Directory.Exists(machineRoot))
        {
            extensionRoots.Add(machineRoot);
        }

        string localSsmsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "SSMS");

        if (Directory.Exists(localSsmsRoot))
        {
            foreach (string root in Directory.EnumerateDirectories(localSsmsRoot, "22.*", SearchOption.TopDirectoryOnly))
            {
                string extensionRoot = Path.Combine(root, "Extensions");
                if (Directory.Exists(extensionRoot))
                {
                    extensionRoots.Add(extensionRoot);
                }
            }
        }

        instances.Add(new SsmsInstance(
            id,
            displayName,
            version,
            normalizedInstallPath,
            extensionRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToList()));
    }

    private static async Task<IReadOnlyList<VswhereInstance>> DetectWithVswhereAsync(CancellationToken cancellationToken)
    {
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string vswherePath = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswherePath))
        {
            return [];
        }

        ProcessStartInfo startInfo = new()
        {
            FileName = vswherePath,
            Arguments = $"-products {SsmsProductId} -all -prerelease -format json",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process process = new() { StartInfo = startInfo };
        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<VswhereInstance>>(output, JsonOptions.Default) ?? [];
    }

    private static string? ReadProductVersion(string installationPath)
    {
        string ssmsExe = Path.Combine(installationPath, "Common7", "IDE", "Ssms.exe");
        return File.Exists(ssmsExe)
            ? FileVersionInfo.GetVersionInfo(ssmsExe).ProductVersion
            : null;
    }

    private sealed record VswhereInstance(
        string? InstanceId,
        string? DisplayName,
        string? InstallationVersion,
        string InstallationPath,
        JsonElement? Catalog)
    {
        public string? ProductId
        {
            get
            {
                if (Catalog is not { ValueKind: JsonValueKind.Object } catalog)
                {
                    return null;
                }

                return catalog.TryGetProperty("productId", out JsonElement value) ? value.GetString() : null;
            }
        }
    }
}
