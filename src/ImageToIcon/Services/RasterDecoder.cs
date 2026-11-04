using System.Runtime.InteropServices;
using BitMiracle.LibTiff.Classic;
using Pfim;
using SkiaSharp;

namespace ImageToIcon.Services;

// Format-aware raster loader. Dispatches to the right decoder based on extension, with
// SkiaSharp as the default for formats it handles natively (BMP, GIF, JPEG, PNG, WebP).
public static class RasterDecoder
{
    public static SKBitmap? TryLoad(string path)
    {
        try
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".qoi" => QoiDecoder.TryDecode(File.ReadAllBytes(path)),
                ".pbm" or ".pgm" or ".ppm" or ".pnm" => PnmDecoder.TryDecode(File.ReadAllBytes(path)),
                ".dib" or ".rle" => DibDecoder.TryDecode(File.ReadAllBytes(path)),
                ".tga" => DecodeViaPfim(path),
                ".tif" or ".tiff" => DecodeViaLibTiff(path),
                _ => SKBitmap.Decode(path)
            };
        }
        catch
        {
            return null;
        }
    }

    private static SKBitmap? DecodeViaPfim(string path)
    {
        using var image = Pfimage.FromFile(path);
        if (image == null) return null;

        var w = image.Width;
        var h = image.Height;
        var src = image.Data;
        var stride = image.Stride;
        var bgra = new byte[(long)w * h * 4];

        switch (image.Format)
        {
            case ImageFormat.Rgba32:
            {
                // Pfim Rgba32 is BGRA little-endian in memory, same layout Skia wants.
                for (var y = 0; y < h; y++)
                    Buffer.BlockCopy(src, y * stride, bgra, y * w * 4, w * 4);
                break;
            }
            case ImageFormat.Rgb24:
            {
                for (var y = 0; y < h; y++)
                {
                    var sr = y * stride;
                    var dr = y * w * 4;
                    for (var x = 0; x < w; x++)
                    {
                        bgra[dr + x * 4] = src[sr + x * 3];
                        bgra[dr + x * 4 + 1] = src[sr + x * 3 + 1];
                        bgra[dr + x * 4 + 2] = src[sr + x * 3 + 2];
                        bgra[dr + x * 4 + 3] = 255;
                    }
                }

                break;
            }
            case ImageFormat.R5g5b5:
            case ImageFormat.R5g5b5a1:
            case ImageFormat.R5g6b5:
            case ImageFormat.Rgb8:
            default:
                return null;
        }

        return WrapBgra(w, h, bgra);
    }

    private static SKBitmap? DecodeViaLibTiff(string path)
    {
        using var tif = Tiff.Open(path, "r");
        if (tif == null) return null;
        var w = tif.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
        var h = tif.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
        if (w <= 0 || h <= 0) return null;

        var raster = new int[w * h];
        if (!tif.ReadRGBAImage(w, h, raster)) return null;

        // libtiff returns ABGR packed in int on little-endian: byte0=R, byte1=G, byte2=B, byte3=A.
        // Also flips Y so raster[0] is bottom-left — undo that.
        var bgra = new byte[(long)w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var srcRow = (h - 1 - y) * w;
            var dstRow = y * w * 4;
            for (var x = 0; x < w; x++)
            {
                var px = raster[srcRow + x];
                bgra[dstRow + x * 4] = (byte)(px >> 16 & 0xFF);
                bgra[dstRow + x * 4 + 1] = (byte)(px >> 8 & 0xFF);
                bgra[dstRow + x * 4 + 2] = (byte)(px & 0xFF);
                bgra[dstRow + x * 4 + 3] = (byte)(px >> 24 & 0xFF);
            }
        }

        return WrapBgra(w, h, bgra);
    }

    private static SKBitmap? WrapBgra(int w, int h, byte[] bgra)
    {
        var bmp = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        if (!bmp.InstallPixels(bmp.Info, handle.AddrOfPinnedObject(), bmp.Info.RowBytes, (_, _) => handle.Free()))
        {
            handle.Free();
            bmp.Dispose();
            return null;
        }

        return bmp;
    }
}