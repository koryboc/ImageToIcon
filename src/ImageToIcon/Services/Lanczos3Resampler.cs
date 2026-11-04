using System.Runtime.InteropServices;
using SkiaSharp;

namespace ImageToIcon.Services;

// Separable Lanczos3 resampler on premultiplied BGRA. Matches the quality profile of
// ImageSharp's KnownResamplers.Lanczos3: 6-tap sinc * sinc(x/3) kernel, filter support
// stretched for downscale so each destination pixel integrates the full source footprint.
public static class Lanczos3Resampler
{
    private const int Radius = 3;

    public static SKBitmap Resize(SKBitmap source, int targetW, int targetH)
    {
        if (targetW <= 0 || targetH <= 0)
            throw new ArgumentOutOfRangeException(nameof(targetW), "Target dimensions must be positive.");

        var srcW = source.Width;
        var srcH = source.Height;

        var premul = ReadPremulFloat(source);

        var xw = PrecomputeWeights(srcW, targetW);
        var yw = PrecomputeWeights(srcH, targetH);

        // Horizontal pass: srcW -> targetW, keep srcH rows.
        var intermediate = new float[(long)targetW * srcH * 4];
        for (var y = 0; y < srcH; y++)
        {
            var srcRow = (long)y * srcW * 4;
            var dstRow = (long)y * targetW * 4;
            for (var x = 0; x < targetW; x++)
            {
                var w = xw[x];
                float b = 0, g = 0, r = 0, a = 0;
                for (var k = 0; k < w.Count; k++)
                {
                    var sx = w.Start + k;
                    if (sx < 0) sx = 0;
                    else if (sx >= srcW) sx = srcW - 1;
                    var off = srcRow + sx * 4;
                    var wk = w.Weights[k];
                    b += premul[off] * wk;
                    g += premul[off + 1] * wk;
                    r += premul[off + 2] * wk;
                    a += premul[off + 3] * wk;
                }

                var o = dstRow + x * 4;
                intermediate[o] = b;
                intermediate[o + 1] = g;
                intermediate[o + 2] = r;
                intermediate[o + 3] = a;
            }
        }

        // Vertical pass: srcH -> targetH, width stays targetW.
        var result = new byte[(long)targetW * targetH * 4];
        for (var y = 0; y < targetH; y++)
        {
            var w = yw[y];
            var dstRow = (long)y * targetW * 4;
            for (var x = 0; x < targetW; x++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (var k = 0; k < w.Count; k++)
                {
                    var sy = w.Start + k;
                    if (sy < 0) sy = 0;
                    else if (sy >= srcH) sy = srcH - 1;
                    var off = (long)sy * targetW * 4 + x * 4;
                    var wk = w.Weights[k];
                    b += intermediate[off] * wk;
                    g += intermediate[off + 1] * wk;
                    r += intermediate[off + 2] * wk;
                    a += intermediate[off + 3] * wk;
                }

                var o = dstRow + x * 4;
                result[o] = ToByte(b);
                result[o + 1] = ToByte(g);
                result[o + 2] = ToByte(r);
                result[o + 3] = ToByte(a);
            }
        }

        return BuildSkBitmap(result, targetW, targetH);
    }

    private static float[] ReadPremulFloat(SKBitmap source)
    {
        var w = source.Width;
        var h = source.Height;
        var buffer = new float[(long)w * h * 4];

        // Draw source into a known-layout Bgra8888/Premul bitmap. SKCanvas handles both
        // the color-type conversion (any source color type -> BGRA) and the alpha-type
        // conversion (unpremul -> premul) in a single pass.
        using var premul = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(premul))
        {
            canvas.Clear(SKColors.Transparent);
            using var img = SKImage.FromBitmap(source);
            canvas.DrawImage(img, 0, 0);
        }

        var pixels = premul.GetPixelSpan();
        for (long i = 0; i < pixels.Length; i++)
            buffer[i] = pixels[(int)i];
        return buffer;
    }

    private static SKBitmap BuildSkBitmap(byte[] bgraPremul, int w, int h)
    {
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        var handle = GCHandle.Alloc(bgraPremul, GCHandleType.Pinned);
        if (!bmp.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes, (_, _) => handle.Free()))
        {
            handle.Free();
            bmp.Dispose();
            throw new InvalidOperationException("Failed to install pixels on SKBitmap.");
        }

        return bmp;
    }

    private static byte ToByte(float f)
    {
        if (f <= 0) return 0;
        if (f >= 255) return 255;
        return (byte)(f + 0.5f);
    }

    private static ContributionRow[] PrecomputeWeights(int srcSize, int dstSize)
    {
        var scale = (double)dstSize / srcSize;
        var filterScale = scale < 1.0 ? scale : 1.0;
        var support = Radius / filterScale;

        var rows = new ContributionRow[dstSize];
        for (var i = 0; i < dstSize; i++)
        {
            var center = (i + 0.5) / scale - 0.5;
            var left = (int)Math.Floor(center - support);
            var right = (int)Math.Ceiling(center + support);
            var count = right - left + 1;
            var weights = new float[count];
            double sum = 0;
            for (var j = 0; j < count; j++)
            {
                var x = (left + j - center) * filterScale;
                var w = Lanczos3(x);
                weights[j] = (float)w;
                sum += w;
            }

            if (sum != 0)
            {
                var inv = 1.0 / sum;
                for (var j = 0; j < count; j++)
                    weights[j] = (float)(weights[j] * inv);
            }

            rows[i] = new ContributionRow(left, weights);
        }

        return rows;
    }

    private static double Lanczos3(double x)
    {
        if (x == 0) return 1.0;
        var ax = Math.Abs(x);
        if (ax >= Radius) return 0;
        var piX = Math.PI * x;
        return Radius * Math.Sin(piX) * Math.Sin(piX / Radius) / (piX * piX);
    }

    private readonly struct ContributionRow(int start, float[] weights)
    {
        public readonly int Start = start;
        public readonly float[] Weights = weights;
        public int Count => Weights.Length;
    }
}