using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace SsmsExtensionManager.App;

public abstract class IconRowBase : INotifyPropertyChanged
{
    private ImageSource? _iconSource;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ImageSource? IconSource => _iconSource;

    public void SetIconSource(ImageSource? iconSource)
    {
        if (ReferenceEquals(_iconSource, iconSource))
        {
            return;
        }

        _iconSource = iconSource;
        OnPropertyChanged(nameof(IconSource));
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

internal static class ExtensionDisplayText
{
    private const string NoDescription = "No description provided.";
    private const string UnknownPublisher = "Unknown publisher";
    private const string UnknownVersion = "Version unknown";

    public static string Description(string? preferredDescription, string? fallbackDescription = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredDescription))
        {
            return preferredDescription;
        }

        return string.IsNullOrWhiteSpace(fallbackDescription)
            ? NoDescription
            : fallbackDescription;
    }

    public static string AuthorText(string? publisher)
        => string.IsNullOrWhiteSpace(publisher)
            ? UnknownPublisher
            : $"by {publisher}";

    public static string VersionText(string? version)
        => string.IsNullOrWhiteSpace(version)
            ? UnknownVersion
            : $"v{version}";

    public static string Initials(string displayName)
    {
        string[] words = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }
}
