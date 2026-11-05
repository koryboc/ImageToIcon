using System.Text;
using SkiaSharp;

namespace ImageToIcon.Services;

public enum FitMode
{
    Fit,
    Stretch,
    Crop
}

// Cross-platform ICO writer. Ported from SilDev.Drawing.IconFactory, now on SkiaSharp.
public static class IconFactory
{
    public const int MaxSize = 4096;
    public const int MinSize = 2;

    private const int SizeIconDir = 6;
    private const int SizeIconDirEntry = 16;

    // Sizes checked by default (Windows 11 application icon set).
    public static readonly IReadOnlyList<int> DefaultSizes =
    [
        256, 48, 32, 24, 16
    ];

    public static IEnumerable<SKBitmap> BuildSizes(SKBitmap source, IEnumerable<int> sizes, double maskRadius = 0, double padding = 0, FitMode mode = FitMode.Fit, double cropOffsetX = 0, double cropOffsetY = 0)
    {
        var maxDim = Math.Max(source.Width, source.Height);
        foreach (var s in sizes.OrderByDescending(x => x))
        {
            if (s > maxDim || s < MinSize || s > MaxSize) continue;
            yield return ResizePadded(source, s, maskRadius, padding, mode, cropOffsetX, cropOffsetY);
        }
    }

    public static SKBitmap ResizePadded(SKBitmap source, int size, double maskRadius = 0, double padding = 0, FitMode mode = FitMode.Fit, double cropOffsetX = 0, double cropOffsetY = 0)
    {
        var padPx = (int)(Math.Clamp(padding, 0, 1) * size);
        var inner = Math.Max(1, size - 2 * padPx);

        int w, h;
        float dx, dy;
        switch (mode)
        {
            case FitMode.Stretch:
                w = inner;
                h = inner;
                dx = padPx;
                dy = padPx;
                break;
            case FitMode.Crop:
                var cover = Math.Max((double)inner / source.Width, (double)inner / source.Height);
                w = Math.Max(1, (int)Math.Round(source.Width * cover));
                h = Math.Max(1, (int)Math.Round(source.Height * cover));
                var panX = Math.Clamp(cropOffsetX, -1.0, 1.0);
                var panY = Math.Clamp(cropOffsetY, -1.0, 1.0);
                dx = padPx + (inner - w) / 2f + (float)(panX * (w - inner) / 2.0);
                dy = padPx + (inner - h) / 2f + (float)(panY * (h - inner) / 2.0);
                break;
            default:
                var fit = Math.Min((double)inner / source.Width, (double)inner / source.Height);
                w = Math.Max(1, (int)Math.Round(source.Width * fit));
                h = Math.Max(1, (int)Math.Round(source.Height * fit));
                dx = padPx + (inner - w) / 2f;
                dy = padPx + (inner - h) / 2f;
                break;
        }

        using var resized = Lanczos3Resampler.Resize(source, w, h);

        var result = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);

        if (maskRadius > 0)
        {
            var r = (float)(Math.Clamp(maskRadius, 0, 1) * inner / 2.0);
            canvas.ClipRoundRect(new SKRoundRect(new SKRect(padPx, padPx, padPx + inner, padPx + inner), r, r), antialias: true);
        }
        else if (mode == FitMode.Crop)
        {
            canvas.ClipRect(new SKRect(padPx, padPx, padPx + inner, padPx + inner));
        }

        using var image = SKImage.FromBitmap(resized);
        canvas.DrawImage(image, new SKRect(dx, dy, dx + w, dy + h));
        canvas.Flush();
        return result;
    }

    public static SKBitmap Copy(SKBitmap source)
    {
        return source.Copy() ?? throw new InvalidOperationException("Failed to copy SKBitmap.");
    }

    public static void Save(IEnumerable<SKBitmap> images, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        Save(images, fs);
    }

    public static void Save(IEnumerable<SKBitmap> images, Stream stream)
    {
        var list = images
                   .Where(i => i is { Width: >= MinSize and <= MaxSize, Height: >= MinSize and <= MaxSize })
                   .OrderByDescending(i => i.Width)
                   .ThenByDescending(i => i.Height)
                   .ToArray();

        if (list.Length == 0)
            throw new InvalidOperationException("No valid images to save.");

        // Encode each frame to PNG up-front so we can compute offsets.
        var buffers = new byte[list.Length][];
        for (var i = 0; i < list.Length; i++)
        {
            using var img = SKImage.FromBitmap(list[i]);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            buffers[i] = data.ToArray();
        }

        using var bw = new BinaryWriter(stream, Encoding.UTF8, true);

        // ICONDIR
        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // type = icon
        bw.Write((ushort)list.Length);

        var offset = (uint)(SizeIconDir + SizeIconDirEntry * list.Length);

        // ICONDIRENTRY records
        for (var i = 0; i < list.Length; i++)
        {
            var img = list[i];
            bw.Write(GetByteDim(img.Width));   // width
            bw.Write(GetByteDim(img.Height));  // height
            bw.Write((byte)0);                 // color count (0 = >= 256)
            bw.Write((byte)0);                 // reserved
            bw.Write((ushort)1);               // color planes
            bw.Write((ushort)32);              // bits per pixel
            bw.Write((uint)buffers[i].Length); // data size
            bw.Write(offset);                  // offset
            offset += (uint)buffers[i].Length;
        }

        for (var i = 0; i < list.Length; i++)
            bw.Write(buffers[i]);
    }

    // ICONDIRENTRY width/height is a single byte; 0 means "256 or larger".
    // The actual dimension of >= 256 frames is stored in the embedded PNG's IHDR.
    private static byte GetByteDim(int v)
    {
        return v >= 256 ? (byte)0 : (byte)v;
    }
}