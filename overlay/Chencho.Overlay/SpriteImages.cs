using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chencho.Core;

namespace Chencho.Overlay;

/// Animations cycle through a few frames; reusing their bitmaps spares WPF a new upload on every frame.
internal static class SpriteImages
{
    private static readonly Dictionary<Pixels, BitmapSource> Cache = new();
    private const int Limit = 512;

    public static BitmapSource For(Pixels pixels)
    {
        if (Cache.TryGetValue(pixels, out var image)) return image;
        if (Cache.Count >= Limit) Cache.Clear();
        image = Render(pixels);
        Cache[pixels.Clone()] = image;
        return image;
    }

    /// One bitmap pixel per sprite pixel; callers scale it with nearest-neighbour sampling.
    public static BitmapSource Render(Pixels pixels)
    {
        var width = pixels.Width;
        var height = pixels.Height;
        var bytes = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[x, y] is not { } color) continue;
                var i = (y * width + x) * 4;
                bytes[i] = color.B;
                bytes[i + 1] = color.G;
                bytes[i + 2] = color.R;
                bytes[i + 3] = 255;
            }
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, bytes, width * 4);
        bitmap.Freeze();
        return bitmap;
    }
}
