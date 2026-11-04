using System.Text;
using SkiaSharp;

namespace ImageToIcon.Services;

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

    public static IEnumerable<SKBitmap> BuildSizes(SKBitmap source, IEnumerable<int> sizes)
    {
        var maxDim = Math.Max(source.Width, source.Height);
        foreach (var s in sizes.OrderByDescending(x => x))
        {
            if (s > maxDim || s < MinSize || s > MaxSize) continue;
            yield return ResizePadded(source, s);
        }
    }

    // Aspect-fit the source into a square of `size`, centered on transparent background.
    // Uses a separable Lanczos3 resampler to match the quality of ImageSharp's
    // KnownResamplers.Lanczos3 (previously used before the SkiaSharp migration).
    public static SKBitmap ResizePadded(SKBitmap source, int size)
    {
        var scale = Math.Min((double)size / source.Width, (double)size / source.Height);
        var w = Math.Max(1, (int)Math.Round(source.Width * scale));
        var h = Math.Max(1, (int)Math.Round(source.Height * scale));

        using var resized = Lanczos3Resampler.Resize(source, w, h);

        var result = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        var dx = (size - w) / 2f;
        var dy = (size - h) / 2f;
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