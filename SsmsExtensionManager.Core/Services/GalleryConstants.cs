namespace SsmsExtensionManager.Core.Services;

public static class GalleryConstants
{
    public const string Host = "ssmsgallery.azurewebsites.net";
    public static readonly Uri FeedUri = new($"https://{Host}/feed/");
}
