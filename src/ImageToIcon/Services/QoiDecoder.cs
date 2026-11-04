using System.Runtime.InteropServices;
using SkiaSharp;

namespace ImageToIcon.Services;

// Minimal QOI decoder per spec at https://qoiformat.org/qoi-specification.pdf.
// Produces an SKBitmap in Bgra8888/Unpremul. Returns null on any format violation.
public static class QoiDecoder
{
    private const byte OpRgb = 0xFE;
    private const byte OpRgba = 0xFF;
    private const byte Mask2 = 0xC0;
    private const byte OpIndex = 0x00;
    private const byte OpDiff = 0x40;
    private const byte OpLuma = 0x80;
    private const byte OpRun = 0xC0;

    public static SKBitmap? TryDecode(byte[] data)
    {
        if (data.Length < 14 + 8) return null;
        if (data[0] != 'q' || data[1] != 'o' || data[2] != 'i' || data[3] != 'f') return null;

        var width = ReadUInt32Be(data, 4);
        var height = ReadUInt32Be(data, 8);
        if (width == 0 || height == 0 || width > 0x10000 || height > 0x10000) return null;

        var pixelCount = (long)width * height;
        var bgra = new byte[pixelCount * 4];
        var index = new (byte r, byte g, byte b, byte a)[64];
        byte pr = 0, pg = 0, pb = 0, pa = 255;

        var p = 14;
        var end = data.Length - 8;
        var run = 0;

        for (long px = 0; px < pixelCount; px++)
        {
            if (run > 0)
            {
                run--;
            }
            else
            {
                if (p >= end) return null;
                var b1 = data[p++];
                if (b1 == OpRgb)
                {
                    if (p + 2 >= end) return null;
                    pr = data[p++];
                    pg = data[p++];
                    pb = data[p++];
                }
                else if (b1 == OpRgba)
                {
                    if (p + 3 >= end) return null;
                    pr = data[p++];
                    pg = data[p++];
                    pb = data[p++];
                    pa = data[p++];
                }
                else
                {
                    switch (b1 & Mask2)
                    {
                        case OpIndex:
                        {
                            var slot = index[b1 & 0x3F];
                            pr = slot.r;
                            pg = slot.g;
                            pb = slot.b;
                            pa = slot.a;
                            break;
                        }
                        case OpDiff:
                        {
                            pr = (byte)(pr + (b1 >> 4 & 0x03) - 2);
                            pg = (byte)(pg + (b1 >> 2 & 0x03) - 2);
                            pb = (byte)(pb + (b1 & 0x03) - 2);
                            break;
                        }
                        case OpLuma:
                        {
                            if (p >= end) return null;
                            var b2 = data[p++];
                            var vg = (b1 & 0x3F) - 32;
                            pr = (byte)(pr + vg - 8 + (b2 >> 4 & 0x0F));
                            pg = (byte)(pg + vg);
                            pb = (byte)(pb + vg - 8 + (b2 & 0x0F));
                            break;
                        }
                        case OpRun:
                        {
                            run = b1 & 0x3F; // 0..61 -> 1..62 more pixels of previous (we already wrote one below)
                            break;
                        }
                    }
                }

                var h = pr * 3 + pg * 5 + pb * 7 + pa * 11 & 63;
                index[h] = (pr, pg, pb, pa);
            }

            var dst = px * 4;
            bgra[dst] = pb;
            bgra[dst + 1] = pg;
            bgra[dst + 2] = pr;
            bgra[dst + 3] = pa;
        }

        var bmp = new SKBitmap(new SKImageInfo((int)width, (int)height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        if (!bmp.InstallPixels(bmp.Info, PinBuffer(bgra, out var handle), bmp.Info.RowBytes, (_, _) => handle.Free()))
        {
            handle.Free();
            bmp.Dispose();
            return null;
        }

        return bmp;
    }

    private static uint ReadUInt32Be(byte[] data, int offset)
    {
        return (uint)data[offset] << 24 |
               (uint)data[offset + 1] << 16 |
               (uint)data[offset + 2] << 8 |
               data[offset + 3];
    }

    private static IntPtr PinBuffer(byte[] buffer, out GCHandle handle)
    {
        handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        return handle.AddrOfPinnedObject();
    }
}