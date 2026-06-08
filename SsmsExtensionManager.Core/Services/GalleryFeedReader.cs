using System.Globalization;
using System.Xml.Linq;
using SsmsExtensionManager.Core.Models;

namespace SsmsExtensionManager.Core.Services;

public sealed class GalleryFeedReader
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Vsx = "http://schemas.microsoft.com/developer/vsx-syndication-schema/2010";

    public IReadOnlyList<GalleryExtension> Read(Stream stream)
    {
        XDocument document = XDocument.Load(stream);
        XElement root = document.Root ?? throw new InvalidDataException("Gallery feed is empty.");

        return root.Elements(Atom + "entry")
            .Select(ReadEntry)
            .Where(extension => extension is not null)
            .Cast<GalleryExtension>()
            .ToList();
    }

    private static GalleryExtension? ReadEntry(XElement entry)
    {
        string? id = Text(entry.Element(Atom + "id"));
        string? title = Text(entry.Element(Atom + "title"));
        Uri? packageUri = UriAttribute(entry.Element(Atom + "content"), "src");

        if (string.IsNullOrWhiteSpace(id) || packageUri is null)
        {
            return null;
        }

        XElement? vsix = entry.Element(Vsx + "Vsix");
        string? version = Text(vsix?.Element(Vsx + "Version"));

        return new GalleryExtension(
            id,
            string.IsNullOrWhiteSpace(title) ? id : title,
            Text(entry.Element(Atom + "summary")),
            Text(entry.Element(Atom + "author")?.Element(Atom + "name")),
            string.IsNullOrWhiteSpace(version) ? "" : version,
            LinkUri(entry, "alternate"),
            packageUri,
            LinkUri(entry, "icon"),
            Date(entry.Element(Atom + "published")),
            Date(entry.Element(Atom + "updated")));
    }

    private static Uri? LinkUri(XElement entry, string rel)
    {
        XElement? link = entry.Elements(Atom + "link")
            .FirstOrDefault(link => string.Equals((string?)link.Attribute("rel"), rel, StringComparison.OrdinalIgnoreCase));
        return UriAttribute(link, "href");
    }

    private static Uri? UriAttribute(XElement? element, string attributeName)
    {
        string? value = element?.Attribute(attributeName)?.Value;
        return Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ? uri : null;
    }

    private static DateTimeOffset? Date(XElement? element)
    {
        string? value = Text(element);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset date)
            ? date
            : null;
    }

    private static string? Text(XElement? element)
    {
        string? value = element?.Value.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
