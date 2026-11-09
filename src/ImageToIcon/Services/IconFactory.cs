using System.Buffers.Binary;
using System.Formats.Tar;
using System.IO.Compression;
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

public readonly record struct StrokeSpec(SKColor Color, double Width, double Offset = 0);

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

    // ic11-ic14 are @2x duplicates of icp5/icp6/ic08/ic09; same PNG written under both OSTypes.
    private static readonly Dictionary<int, string[]> IcnsTypesBySize = new()
    {
        { 16, ["icp4"] },
        { 32, ["icp5", "ic11"] },
        { 64, ["icp6", "ic12"] },
        { 128, ["ic07"] },
        { 256, ["ic08", "ic13"] },
        { 512, ["ic09", "ic14"] },
        { 1024, ["ic10"] }
    };

    // freedesktop.org app-icon sizes placed under hicolor/<size>x<size>/apps/.
    public static readonly IReadOnlyList<int> DefaultHicolorSizes =
    [
        512, 256, 128, 64, 48, 32, 24, 22, 16
    ];

    public static IEnumerable<SKBitmap> BuildSizes(SKBitmap source, IEnumerable<int> sizes, double maskRadius = 0, double padding = 0, FitMode mode = FitMode.Fit, double cropOffsetX = 0, double cropOffsetY = 0, double cropZoom = 1, SKColor? background = null, ShadowSpec? shadow = null, StrokeSpec? stroke = null, bool shadowIncludesBackground = false, bool strokeIncludesBackground = false, bool backgroundFillsCanvas = false)
    {
        var maxDim = Math.Max(source.Width, source.Height);
        foreach (var s in sizes.OrderByDescending(x => x))
        {
            if (s > maxDim || s < MinSize || s > MaxSize) continue;
            yield return ResizePadded(source, s, maskRadius, padding, mode, cropOffsetX, cropOffsetY, cropZoom, background, shadow, stroke, shadowIncludesBackground, strokeIncludesBackground, backgroundFillsCanvas);
        }
    }

    public static SKBitmap ResizePadded(SKBitmap source, int size, double maskRadius = 0, double padding = 0, FitMode mode = FitMode.Fit, double cropOffsetX = 0, double cropOffsetY = 0, double cropZoom = 1, SKColor? background = null, ShadowSpec? shadow = null, StrokeSpec? stroke = null, bool shadowIncludesBackground = false, bool strokeIncludesBackground = false, bool backgroundFillsCanvas = false)
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
        var clipRect = new SKRect(padPx, padPx, padPx + inner, padPx + inner);
        var imageRect = new SKRect(dx, dy, dx + w, dy + h);
        var cornerR = maskRadius > 0 ? (float)(Math.Clamp(maskRadius, 0, 1) * inner / 2.0) : 0f;
        var hasMask = maskRadius > 0;

        var hasShadow = shadow is { Color.Alpha: > 0 };
        var hasStroke = stroke is { Color.Alpha: > 0, Width: > 0 };
        var hasBg = background is not null;
        var bgFill = hasBg && backgroundFillsCanvas;
        var bgShape = hasBg && !backgroundFillsCanvas;
        var shadowWithBg = bgShape && shadowIncludesBackground;
        var strokeWithBg = bgShape && strokeIncludesBackground;

        canvas.Clear(bgFill ? background!.Value : SKColors.Transparent);

        if (!hasShadow && !hasStroke)
        {
            canvas.Save();
            if (hasMask)
                canvas.ClipRoundRect(new SKRoundRect(clipRect, cornerR, cornerR), antialias: true);
            else if (bgShape || mode == FitMode.Crop)
                canvas.ClipRect(clipRect);
            if (bgShape)
            {
                using var bgPaint0 = new SKPaint();
                bgPaint0.Color = background!.Value;
                bgPaint0.IsAntialias = true;
                canvas.DrawRect(clipRect, bgPaint0);
            }

            using var img0 = SKImage.FromBitmap(resized);
            canvas.DrawImage(img0, imageRect);
            canvas.Restore();
            canvas.Flush();
            return result;
        }

        SKBitmap BuildLayer(bool withBg)
        {
            var bmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var lc = new SKCanvas(bmp);
            lc.Clear(SKColors.Transparent);
            if (hasMask)
                lc.ClipRoundRect(new SKRoundRect(clipRect, cornerR, cornerR), antialias: true);
            else if (mode == FitMode.Crop || withBg)
                lc.ClipRect(clipRect);
            if (withBg)
            {
                using var bgPaint = new SKPaint();
                bgPaint.Color = background!.Value;
                bgPaint.IsAntialias = true;
                lc.DrawRect(clipRect, bgPaint);
            }

            using var img = SKImage.FromBitmap(resized);
            lc.DrawImage(img, imageRect);
            return bmp;
        }

        SKBitmap? imageLayerBmp = null, bgLayerBmp = null;
        SKImage? imageLayerImg = null, bgLayerImg = null;

        SKImage LayerFor(bool withBg)
        {
            if (withBg)
            {
                if (bgLayerImg != null) return bgLayerImg;
                bgLayerBmp = BuildLayer(true);
                bgLayerImg = SKImage.FromBitmap(bgLayerBmp);
                return bgLayerImg;
            }

            if (imageLayerImg != null) return imageLayerImg;
            imageLayerBmp = BuildLayer(false);
            imageLayerImg = SKImage.FromBitmap(imageLayerBmp);
            return imageLayerImg;
        }

        void DrawShadow()
        {
            if (!hasShadow) return;
            var sh = shadow!.Value;
            var sigma = (float)Math.Max(0, sh.Blur * size);
            using var shadowPaint = new SKPaint();
            shadowPaint.ImageFilter = SKImageFilter.CreateDropShadowOnly((float)(sh.OffsetX * size), (float)(sh.OffsetY * size), sigma, sigma, sh.Color);
            canvas.DrawImage(LayerFor(shadowWithBg), 0, 0, shadowPaint);
        }

        void DrawBg()
        {
            if (!bgShape) return;
            canvas.Save();
            if (hasMask)
                canvas.ClipRoundRect(new SKRoundRect(clipRect, cornerR, cornerR), antialias: true);
            else
                canvas.ClipRect(clipRect);
            using var bgPaint = new SKPaint();
            bgPaint.Color = background!.Value;
            bgPaint.IsAntialias = true;
            canvas.DrawRect(clipRect, bgPaint);
            canvas.Restore();
        }

        try
        {
            if (shadowWithBg)
            {
                DrawShadow();
                DrawBg();
            }
            else
            {
                DrawBg();
                DrawShadow();
            }

            float innerR = 0;
            if (hasStroke)
            {
                var st = stroke!.Value;
                var r = (float)Math.Max(0.5, st.Width * size);
                var off = (float)Math.Clamp(st.Offset, 0, 1);
                var outerR = r * (1 - off);
                innerR = r * off;

                if (outerR > 0.05f)
                {
                    var subject = LayerFor(strokeWithBg);

                    using var dilated = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                    using (var dc = new SKCanvas(dilated))
                    {
                        dc.Clear(SKColors.Transparent);
                        using var dp = new SKPaint();
                        dp.ImageFilter = SKImageFilter.CreateDilate(outerR, outerR);
                        dc.DrawImage(subject, 0, 0, dp);
                    }

                    using var dilatedImg = SKImage.FromBitmap(dilated);

                    using var outerRing = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                    using (var orc = new SKCanvas(outerRing))
                    {
                        orc.Clear(SKColors.Transparent);
                        using var fillPaint = new SKPaint();
                        fillPaint.ColorFilter = SKColorFilter.CreateBlendMode(st.Color, SKBlendMode.SrcIn);
                        fillPaint.IsAntialias = true;
                        orc.DrawImage(dilatedImg, 0, 0, fillPaint);

                        using var cutPaint = new SKPaint();
                        cutPaint.BlendMode = SKBlendMode.DstOut;
                        orc.DrawImage(subject, 0, 0, cutPaint);
                    }

                    using var outerRingImg = SKImage.FromBitmap(outerRing);
                    canvas.DrawImage(outerRingImg, 0, 0);
                }
            }

            canvas.Save();
            if (hasMask)
                canvas.ClipRoundRect(new SKRoundRect(clipRect, cornerR, cornerR), antialias: true);
            else if (mode == FitMode.Crop)
                canvas.ClipRect(clipRect);
            using (var finalImg = SKImage.FromBitmap(resized))
            {
                canvas.DrawImage(finalImg, imageRect);
            }

            canvas.Restore();

            if (hasStroke && innerR > 0.05f)
            {
                var st = stroke!.Value;
                var subject = LayerFor(strokeWithBg);

                using var eroded = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                using (var ec = new SKCanvas(eroded))
                {
                    ec.Clear(SKColors.Transparent);
                    using var ep = new SKPaint();
                    ep.ImageFilter = SKImageFilter.CreateErode(innerR, innerR);
                    ec.DrawImage(subject, 0, 0, ep);
                }

                using var erodedImg = SKImage.FromBitmap(eroded);

                using var ring = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                using (var rc = new SKCanvas(ring))
                {
                    rc.Clear(SKColors.Transparent);
                    using var fillPaint = new SKPaint();
                    fillPaint.ColorFilter = SKColorFilter.CreateBlendMode(st.Color, SKBlendMode.SrcIn);
                    fillPaint.IsAntialias = true;
                    rc.DrawImage(subject, 0, 0, fillPaint);

                    using var cutPaint = new SKPaint();
                    cutPaint.BlendMode = SKBlendMode.DstOut;
                    rc.DrawImage(erodedImg, 0, 0, cutPaint);
                }

                using var ringImg = SKImage.FromBitmap(ring);
                canvas.DrawImage(ringImg, 0, 0);
            }

            canvas.Flush();
            return result;
        }
        finally
        {
            imageLayerImg?.Dispose();
            bgLayerImg?.Dispose();
            imageLayerBmp?.Dispose();
            bgLayerBmp?.Dispose();
        }
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

    public static void SaveHicolorFolder(IEnumerable<SKBitmap> images, string directory, string baseName)
    {
        foreach (var (size, png) in EncodeHicolorFrames(images))
        {
            var dir = Path.Combine(directory, $"{size}x{size}", "apps");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, baseName + ".png"), png);
        }
    }

    public static void SaveHicolorArchive(IEnumerable<SKBitmap> images, string path, string baseName)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var gz = new GZipStream(fs, CompressionLevel.Optimal);
        using var tar = new TarWriter(gz, TarEntryFormat.Ustar);
        foreach (var (size, png) in EncodeHicolorFrames(images))
        {
            var entry = new UstarTarEntry(TarEntryType.RegularFile, $"hicolor/{size}x{size}/apps/{baseName}.png")
            {
                DataStream = new MemoryStream(png),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead
            };
            tar.WriteEntry(entry);
        }
    }

    private static IEnumerable<(int Size, byte[] Png)> EncodeHicolorFrames(IEnumerable<SKBitmap> images)
    {
        var list = images
                   .Where(i => i.Width == i.Height && i.Width is >= MinSize and <= MaxSize)
                   .GroupBy(i => i.Width)
                   .Select(g => g.First())
                   .OrderByDescending(i => i.Width)
                   .ToArray();

        if (list.Length == 0)
            throw new InvalidOperationException("No valid images to save.");

        foreach (var bmp in list)
        {
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            yield return (bmp.Width, data.ToArray());
        }
    }

    public static void SaveIcns(IEnumerable<SKBitmap> images, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        SaveIcns(images, fs);
    }

    public static void SaveIcns(IEnumerable<SKBitmap> images, Stream stream)
    {
        var list = images
                   .Where(i => i.Width == i.Height && IcnsTypesBySize.ContainsKey(i.Width))
                   .GroupBy(i => i.Width)
                   .Select(g => g.First())
                   .OrderByDescending(i => i.Width)
                   .ToArray();

        if (list.Length == 0)
            throw new InvalidOperationException("No valid images to save.");

        var buffers = new byte[list.Length][];
        for (var i = 0; i < list.Length; i++)
        {
            using var img = SKImage.FromBitmap(list[i]);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            buffers[i] = data.ToArray();
        }

        var totalSize = 8;
        for (var i = 0; i < list.Length; i++)
            totalSize += IcnsTypesBySize[list[i].Width].Length * (8 + buffers[i].Length);

        Span<byte> header = stackalloc byte[8];
        Encoding.ASCII.GetBytes("icns", header[..4]);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)totalSize);
        stream.Write(header);

        Span<byte> chunkHeader = stackalloc byte[8];
        for (var i = 0; i < list.Length; i++)
        {
            foreach (var type in IcnsTypesBySize[list[i].Width])
            {
                Encoding.ASCII.GetBytes(type, chunkHeader[..4]);
                BinaryPrimitives.WriteUInt32BigEndian(chunkHeader[4..], (uint)(8 + buffers[i].Length));
                stream.Write(chunkHeader);
                stream.Write(buffers[i]);
            }
        }
    }
}
