using SkiaSharp;
using Svg.Skia;

namespace ImageToIcon.Services;

public static class SvgLoader
{
    public static readonly string[] Extensions = [".svg", ".svgz"];

    public static bool IsSupported(string path)
    {
        return Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    public static SKBitmap? TryLoad(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return TryLoad(fs);
        }
        catch
        {
            return null;
        }
    }

    public static SKBitmap? TryLoad(Stream stream)
    {
        try
        {
            using var svg = new SKSvg();
            var pic = svg.Load(stream);
            if (pic == null)
                return null;

            var bounds = pic.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            const int target = IconFactory.MaxSize;
            var scale = target / Math.Max(bounds.Width, bounds.Height);
            var w = Math.Max(1, (int)Math.Round(bounds.Width * scale));
            var h = Math.Max(1, (int)Math.Round(bounds.Height * scale));

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bmp = new SKBitmap(info);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.DrawPicture(pic);
            canvas.Flush();
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}