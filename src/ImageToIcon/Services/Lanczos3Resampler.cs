using System.Buffers;
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

        // Normalize to BGRA8888/Premul once; skip if already matching.
        SKBitmap? converted = null;
        SKBitmap src;
        if (source.ColorType == SKColorType.Bgra8888 && source.AlphaType == SKAlphaType.Premul)
        {
            src = source;
        }
        else
        {
            converted = new SKBitmap(new SKImageInfo(srcW, srcH, SKColorType.Bgra8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(converted))
            {
                canvas.Clear(SKColors.Transparent);
                using var img = SKImage.FromBitmap(source);
                canvas.DrawImage(img, 0, 0);
            }

            src = converted;
        }

        try
        {
            var xw = PrecomputeWeights(srcW, targetW);
            var yw = PrecomputeWeights(srcH, targetH);

            var intermediateLen = checked(targetW * srcH * 4);
            var intermediate = ArrayPool<float>.Shared.Rent(intermediateLen);
            var interHandle = GCHandle.Alloc(intermediate, GCHandleType.Pinned);
            try
            {
                var interAddr = interHandle.AddrOfPinnedObject();
                HorizontalPass(src, srcW, srcH, targetW, xw, interAddr);
                return VerticalPass(interAddr, targetW, srcH, targetH, yw);
            }
            finally
            {
                interHandle.Free();
                ArrayPool<float>.Shared.Return(intermediate);
            }
        }
        finally
        {
            converted?.Dispose();
        }
    }

    private unsafe static void HorizontalPass(SKBitmap src, int srcW, int srcH, int targetW, ContributionRow[] xw, IntPtr interAddr)
    {
        var srcAddr = src.GetPixels(out _);
        var srcStride = src.RowBytes;

        Parallel.For(0, srcH, y =>
        {
            var rowPtr = (byte*)srcAddr + (long)y * srcStride;
            var dstRow = (float*)interAddr + (long)y * targetW * 4;
            for (var x = 0; x < targetW; x++)
            {
                var w = xw[x];
                var weights = w.Weights;
                float b = 0, g = 0, r = 0, a = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var sx = w.Start + k;
                    if (sx < 0) sx = 0;
                    else if (sx >= srcW) sx = srcW - 1;
                    var p = rowPtr + sx * 4;
                    var wk = weights[k];
                    b += p[0] * wk;
                    g += p[1] * wk;
                    r += p[2] * wk;
                    a += p[3] * wk;
                }

                var o = dstRow + x * 4;
                o[0] = b;
                o[1] = g;
                o[2] = r;
                o[3] = a;
            }
        });
    }

    private unsafe static SKBitmap VerticalPass(IntPtr interAddr, int targetW, int srcH, int targetH, ContributionRow[] yw)
    {
        var info = new SKImageInfo(targetW, targetH, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        var dstAddr = bmp.GetPixels(out _);
        var dstStride = bmp.RowBytes;

        Parallel.For(0, targetH, y =>
        {
            var w = yw[y];
            var weights = w.Weights;
            var rowPtr = (byte*)dstAddr + (long)y * dstStride;
            for (var x = 0; x < targetW; x++)
            {
                float b = 0, g = 0, r = 0, a = 0;
                for (var k = 0; k < weights.Length; k++)
                {
                    var sy = w.Start + k;
                    if (sy < 0) sy = 0;
                    else if (sy >= srcH) sy = srcH - 1;
                    var p = (float*)interAddr + ((long)sy * targetW + x) * 4;
                    var wk = weights[k];
                    b += p[0] * wk;
                    g += p[1] * wk;
                    r += p[2] * wk;
                    a += p[3] * wk;
                }

                var o = rowPtr + x * 4;
                o[0] = ToByte(b);
                o[1] = ToByte(g);
                o[2] = ToByte(r);
                o[3] = ToByte(a);
            }
        });

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
    }
}