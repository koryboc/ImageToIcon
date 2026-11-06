using System.Text;
using SkiaSharp;

namespace ImageToIcon.Services;

public enum FitMode
{
    Fit,
    Stretch,
    Crop
}

// All offsets/blur/width are fractions of the output size (0..1).
public readonly record struct ShadowSpec(SKColor Color, double OffsetX, double OffsetY, double Blur);

public readonly record struct StrokeSpec(SKColor Color, double Width);

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

    public static IEnumerable<SKBitmap> BuildSizes(SKBitmap source, IEnumerable<int> sizes, double maskRadius = 0, double padding = 0, FitMode mode = FitMode.Fit, double cropOffsetX = 0, double cropOffsetY = 0, double cropZoom = 1, SKColor? background = null, ShadowSpec? shadow = null, StrokeSpec? stroke = null)
    {
        var maxDim = Math.Max(source.Width, source.Height);
        foreach (var s in sizes.OrderByDescending(x => x))
        {
            if (s > maxDim || s < MinSize || s > MaxSize) continue;
            yield return ResizePadded(source, s, maskRadius, padding, mode, cropOffsetX, cropOffsetY, cropZoom, background, shadow, stroke);
        }
    }

    public static SKBitmap ResizePadded(SKBitmap source, int size, double maskRadius = 0, double padding = 0, FitMode mode = FitMode.Fit, double cropOffsetX = 0, double cropOffsetY = 0, double cropZoom = 1, SKColor? background = null, ShadowSpec? shadow = null, StrokeSpec? stroke = null)
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
                var zoom = Math.Max(1.0, cropZoom);
                var cover = Math.Max((double)inner / source.Width, (double)inner / source.Height) * zoom;
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
        canvas.Clear(background ?? SKColors.Transparent);

        var clipRect = new SKRect(padPx, padPx, padPx + inner, padPx + inner);
        var imageRect = new SKRect(dx, dy, dx + w, dy + h);
        var cornerR = maskRadius > 0 ? (float)(Math.Clamp(maskRadius, 0, 1) * inner / 2.0) : 0f;
        var hasMask = maskRadius > 0;

        var hasShadow = shadow is { Color.Alpha: > 0 };
        var hasStroke = stroke is { Color.Alpha: > 0, Width: > 0 };

        if (hasShadow || hasStroke)
        {
            using var layer = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var lc = new SKCanvas(layer))
            {
                lc.Clear(SKColors.Transparent);
                if (hasMask)
                    lc.ClipRoundRect(new SKRoundRect(clipRect, cornerR, cornerR), antialias: true);
                else if (mode == FitMode.Crop)
                    lc.ClipRect(clipRect);
                using var img = SKImage.FromBitmap(resized);
                lc.DrawImage(img, imageRect);
            }

            using var layerImg = SKImage.FromBitmap(layer);

            if (hasShadow)
            {
                var sh = shadow!.Value;
                var sigma = (float)Math.Max(0, sh.Blur * size);
                using var shadowPaint = new SKPaint();
                shadowPaint.ImageFilter = SKImageFilter.CreateDropShadowOnly((float)(sh.OffsetX * size), (float)(sh.OffsetY * size), sigma, sigma, sh.Color);
                canvas.DrawImage(layerImg, 0, 0, shadowPaint);
            }

            if (hasStroke)
            {
                var st = stroke!.Value;
                var r = (float)Math.Max(0.5, st.Width * size);
                using var strokePaint = new SKPaint();
                strokePaint.ImageFilter = SKImageFilter.CreateDilate(r, r);
                strokePaint.ColorFilter = SKColorFilter.CreateBlendMode(st.Color, SKBlendMode.SrcIn);
                strokePaint.IsAntialias = true;
                canvas.DrawImage(layerImg, 0, 0, strokePaint);
            }

            canvas.DrawImage(layerImg, 0, 0);
        }
        else
        {
            canvas.Save();
            if (hasMask)
                canvas.ClipRoundRect(new SKRoundRect(clipRect, cornerR, cornerR), antialias: true);
            else if (mode == FitMode.Crop)
                canvas.ClipRect(clipRect);
            using var img = SKImage.FromBitmap(resized);
            canvas.DrawImage(img, imageRect);
            canvas.Restore();
        }

        canvas.Flush();
        return result;
    }

    public static SKBitmap Copy(SKBitmap source)
    {
        return source.Copy() ?? throw new InvalidOperationException("Failed to copy SKBitmap.");
    }

    public static bool HasTransparency(SKBitmap? bmp)
    {
        if (bmp == null || bmp.AlphaType == SKAlphaType.Opaque)
            return false;
        using var pm = bmp.PeekPixels();
        if (pm == null)
            return false;
        var span = pm.GetPixelSpan();
        var info = bmp.Info;
        var rowBytes = pm.RowBytes;
        var w = info.Width;
        var h = info.Height;

        switch (info.ColorType)
        {
            case SKColorType.Bgra8888:
            case SKColorType.Rgba8888:
                for (var y = 0; y < h; y++)
                {
                    var row = span.Slice(y * rowBytes, w * 4);
                    for (var x = 0; x < w; x++)
                        if (row[x * 4 + 3] < 255)
                            return true;
                }

                return false;
            default:
                using (var copy = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul)))
                {
                    if (!bmp.ScalePixels(copy, new SKSamplingOptions(SKFilterMode.Nearest)))
                        return false;
                    return HasTransparency(copy);
                }
        }
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