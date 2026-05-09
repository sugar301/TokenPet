using SkiaSharp;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokenPet.Services;

public static class SpriteLoader
{
    public static BitmapSource? LoadSpritesheet(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var ext = Path.GetExtension(path).ToLower();
            if (ext is ".webp")
                return LoadWebP(path);
            if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp")
                return LoadStandard(path);
            return null;
        }
        catch { return null; }
    }

    private static BitmapSource LoadStandard(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static BitmapSource LoadWebP(string path)
    {
        using var skBitmap = SKBitmap.Decode(path);
        if (skBitmap == null) throw new Exception("Failed to decode WebP");

        using var image = SKImage.FromBitmap(skBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];
        source.Freeze();
        return source;
    }
}
