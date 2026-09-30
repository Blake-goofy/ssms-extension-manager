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

        string defaultPath = SsmsPaths.DefaultInstallationPath;

        if (Directory.Exists(defaultPath))
        {
            AddInstance(instances, SsmsPaths.DefaultInstanceId, SsmsPaths.DefaultDisplayName, ReadProductVersion(defaultPath), defaultPath);
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
        string localSsmsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "SSMS");

        instances.Add(new SsmsInstance(
            id,
            displayName,
            version,
            normalizedInstallPath,
            GetExtensionRoots(normalizedInstallPath, id, localSsmsRoot)));
    }

    internal static IReadOnlyList<string> GetExtensionRoots(string installationPath, string instanceId, string localSsmsRoot)
    {
        List<string> extensionRoots = [];
        string machineRoot = SsmsPaths.GetMachineExtensionRoot(installationPath);
        if (Directory.Exists(machineRoot))
        {
            extensionRoots.Add(machineRoot);
        }

        // ponytail: without a vswhere instance ID, omit per-user roots; an authoritative ID is needed to avoid stale profiles.
        if (instanceId.Length == 8 && instanceId.All(char.IsAsciiHexDigit))
        {
            string perUserRoot = Path.Combine(localSsmsRoot, $"22.0_{instanceId}", "Extensions");
            if (Directory.Exists(perUserRoot))
            {
                extensionRoots.Add(perUserRoot);
            }
        }

        return extensionRoots;
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
        string ssmsExe = SsmsPaths.GetExecutablePath(installationPath);
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
