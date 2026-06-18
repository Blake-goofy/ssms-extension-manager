namespace SsmsExtensionManager.Core.Services;

public static class SsmsPaths
{
    public const string ExecutableFileName = "SSMS.exe";
    public const string VsixInstallerFileName = "VSIXInstaller.exe";
    public const string DefaultInstanceId = "SSMS22.Default";
    public const string DefaultDisplayName = "SQL Server Management Studio 22";

    private const string SsmsInstallDirectoryName = "Microsoft SQL Server Management Studio 22";
    private const string ReleaseDirectoryName = "Release";
    private const string Common7DirectoryName = "Common7";
    private const string IdeDirectoryName = "IDE";
    private const string ExtensionsDirectoryName = "Extensions";

    public static string DefaultInstallationPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        SsmsInstallDirectoryName,
        ReleaseDirectoryName);

    public static string GetIdePath(string installationPath)
        => Path.Combine(installationPath, Common7DirectoryName, IdeDirectoryName);

    public static string GetExecutablePath(string installationPath)
        => Path.Combine(GetIdePath(installationPath), ExecutableFileName);

    public static string GetDefaultExecutablePath()
        => GetExecutablePath(DefaultInstallationPath);

    public static string GetVsixInstallerPath(string installationPath)
        => Path.Combine(GetIdePath(installationPath), VsixInstallerFileName);

    public static string GetMachineExtensionRoot(string installationPath)
        => Path.Combine(GetIdePath(installationPath), ExtensionsDirectoryName);
}
