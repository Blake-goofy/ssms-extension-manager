using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace SsmsExtensionManager.App;

internal sealed class GalleryIconLoader(HttpClient httpClient)
{
    private const int TargetPixelSize = 128;
    private readonly ConcurrentDictionary<Uri, Task<ImageSource?>> _cache = new();

    public Task<ImageSource?> LoadAsync(Uri? iconUri, CancellationToken cancellationToken = default)
    {
        if (iconUri is null || string.Equals(Path.GetExtension(iconUri.LocalPath), ".svg", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        return _cache.GetOrAdd(iconUri, uri => LoadUncachedAsync(uri, cancellationToken));
    }

    private async Task<ImageSource?> LoadUncachedAsync(Uri iconUri, CancellationToken cancellationToken)
    {
        try
        {
            byte[] bytes = await httpClient.GetByteArrayAsync(iconUri, cancellationToken);
            using SKBitmap? sourceBitmap = SKBitmap.Decode(bytes);
            if (sourceBitmap is null || sourceBitmap.Width <= 0 || sourceBitmap.Height <= 0)
            {
                return null;
            }

            // Keep SkiaSharp here: WPF's BitmapImage/DrawingVisual resize path made downloaded
            // gallery icons visibly pixelated when centered into the 128px transparent square.
            using SKImage sourceImage = SKImage.FromBitmap(sourceBitmap);
            using SKSurface surface = SKSurface.Create(new SKImageInfo(TargetPixelSize, TargetPixelSize, SKColorType.Bgra8888, SKAlphaType.Premul));
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);

            SKRect destination = FitWithinSquare(sourceBitmap.Width, sourceBitmap.Height, TargetPixelSize);
            SKSamplingOptions sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
            using SKPaint paint = new() { IsAntialias = true };

            canvas.DrawImage(sourceImage, destination, sampling, paint);
            canvas.Flush();

            using SKImage resizedImage = surface.Snapshot();
            using SKData encodedImage = resizedImage.Encode(SKEncodedImageFormat.Png, 100);
            await using Stream stream = encodedImage.AsStream();

            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static SKRect FitWithinSquare(int width, int height, int squareSize)
    {
        float scale = Math.Min((float)squareSize / width, (float)squareSize / height);
        float scaledWidth = width * scale;
        float scaledHeight = height * scale;
        float left = (squareSize - scaledWidth) / 2;
        float top = (squareSize - scaledHeight) / 2;
        return new SKRect(left, top, left + scaledWidth, top + scaledHeight);
    }
}
