using System.IO.Compression;
using System.Xml.Linq;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class VsixManifestReader
{
    private static readonly XName PackageManifest = XName.Get("PackageManifest", "http://schemas.microsoft.com/developer/vsx-schema/2011");
    private static readonly XName Metadata = XName.Get("Metadata", "http://schemas.microsoft.com/developer/vsx-schema/2011");
    private static readonly XName Identity = XName.Get("Identity", "http://schemas.microsoft.com/developer/vsx-schema/2011");
    private static readonly XName DisplayName = XName.Get("DisplayName", "http://schemas.microsoft.com/developer/vsx-schema/2011");
    private static readonly XName Description = XName.Get("Description", "http://schemas.microsoft.com/developer/vsx-schema/2011");
    private static readonly XName MoreInfo = XName.Get("MoreInfo", "http://schemas.microsoft.com/developer/vsx-schema/2011");
    private static readonly XName ReleaseNotes = XName.Get("ReleaseNotes", "http://schemas.microsoft.com/developer/vsx-schema/2011");

    public VsixManifest ReadFromVsix(string vsixPath)
    {
        using ZipArchive archive = ZipFile.OpenRead(vsixPath);
        ZipArchiveEntry manifestEntry = archive.GetEntry("extension.vsixmanifest")
            ?? throw new InvalidDataException($"VSIX does not contain extension.vsixmanifest: {vsixPath}");

        using Stream stream = manifestEntry.Open();
        return Read(stream);
    }

    public VsixManifest ReadFromInstalledFolder(string extensionFolder)
    {
        string manifestPath = Path.Combine(extensionFolder, "extension.vsixmanifest");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Installed extension manifest was not found.", manifestPath);
        }

        using FileStream stream = File.OpenRead(manifestPath);
        return Read(stream);
    }

    public VsixManifest Read(Stream stream)
    {
        XDocument document = XDocument.Load(stream);
        XElement root = document.Root ?? throw new InvalidDataException("VSIX manifest is empty.");

        if (root.Name.LocalName != PackageManifest.LocalName)
        {
            throw new InvalidDataException("File is not a VSIX package manifest.");
        }

        XElement metadata = FindChild(root, Metadata.LocalName)
            ?? throw new InvalidDataException("VSIX manifest is missing Metadata.");
        XElement identity = FindChild(metadata, Identity.LocalName)
            ?? throw new InvalidDataException("VSIX manifest is missing Metadata/Identity.");

        string id = RequiredAttribute(identity, "Id");
        string version = RequiredAttribute(identity, "Version");
        string publisher = RequiredAttribute(identity, "Publisher");
        string? displayName = FindChild(metadata, DisplayName.LocalName)?.Value.Trim();

        return new VsixManifest(
            id,
            version,
            publisher,
            string.IsNullOrWhiteSpace(displayName) ? id : displayName!,
            ValueNormalization.EmptyToNull(FindChild(metadata, Description.LocalName)?.Value),
            ValueNormalization.EmptyToNull(FindChild(metadata, MoreInfo.LocalName)?.Value),
            ValueNormalization.EmptyToNull(FindChild(metadata, ReleaseNotes.LocalName)?.Value));
    }

    private static XElement? FindChild(XElement element, string localName)
    {
        return element.Elements().FirstOrDefault(child => child.Name.LocalName == localName);
    }

    private static string RequiredAttribute(XElement element, string name)
    {
        string? value = element.Attribute(name)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"VSIX manifest Identity is missing {name}.");
        }

        return value.Trim();
    }
}
