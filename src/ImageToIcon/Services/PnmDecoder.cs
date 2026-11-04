using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using SkiaSharp;

namespace ImageToIcon.Services;

// Netpbm decoder for P1..P6 (PBM/PGM/PPM, ASCII + binary).
public static class PnmDecoder
{
    public static SKBitmap? TryDecode(byte[] data)
    {
        if (data.Length < 3 || data[0] != 'P') return null;
        var magic = (char)data[1];
        if (magic is < '1' or > '6') return null;

        var pos = 2;
        if (!TryReadInt(data, ref pos, out var width) || width <= 0) return null;
        if (!TryReadInt(data, ref pos, out var height) || height <= 0) return null;

        var maxVal = 1;
        if (magic != '1' && magic != '4')
        {
            if (!TryReadInt(data, ref pos, out maxVal) || maxVal is <= 0 or > 65535) return null;
        }

        // For binary formats, exactly one whitespace byte separates header and pixel data.
        var binary = magic is '4' or '5' or '6';
        if (binary)
        {
            if (pos >= data.Length || !IsWhitespace(data[pos])) return null;
            pos++;
        }

        var pixels = new byte[(long)width * height * 4];
        return magic switch
        {
            '1' => ReadAsciiBitmap(data, pos, width, height, pixels),
            '2' => ReadAsciiGray(data, pos, width, height, maxVal, pixels),
            '3' => ReadAsciiRgb(data, pos, width, height, maxVal, pixels),
            '4' => ReadBinaryBitmap(data, pos, width, height, pixels),
            '5' => ReadBinaryGray(data, pos, width, height, maxVal, pixels),
            '6' => ReadBinaryRgb(data, pos, width, height, maxVal, pixels),
            _ => null
        };
    }

    private static SKBitmap? Build(int width, int height, byte[] bgra)
    {
        var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        if (!bmp.InstallPixels(bmp.Info, handle.AddrOfPinnedObject(), bmp.Info.RowBytes, (_, _) => handle.Free()))
        {
            handle.Free();
            bmp.Dispose();
            return null;
        }

        return bmp;
    }

    private static SKBitmap? ReadAsciiBitmap(byte[] data, int pos, int w, int h, byte[] bgra)
    {
        var i = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (!TryReadInt(data, ref pos, out var v)) return null;
                var c = v == 0 ? (byte)255 : (byte)0;
                bgra[i++] = c;
                bgra[i++] = c;
                bgra[i++] = c;
                bgra[i++] = 255;
            }

        return Build(w, h, bgra);
    }

    private static SKBitmap? ReadAsciiGray(byte[] data, int pos, int w, int h, int maxVal, byte[] bgra)
    {
        var i = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (!TryReadInt(data, ref pos, out var v)) return null;
                var c = (byte)Math.Clamp(v * 255 / maxVal, 0, 255);
                bgra[i++] = c;
                bgra[i++] = c;
                bgra[i++] = c;
                bgra[i++] = 255;
            }

        return Build(w, h, bgra);
    }

    private static SKBitmap? ReadAsciiRgb(byte[] data, int pos, int w, int h, int maxVal, byte[] bgra)
    {
        var i = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (!TryReadInt(data, ref pos, out var r)) return null;
                if (!TryReadInt(data, ref pos, out var g)) return null;
                if (!TryReadInt(data, ref pos, out var b)) return null;
                bgra[i++] = (byte)Math.Clamp(b * 255 / maxVal, 0, 255);
                bgra[i++] = (byte)Math.Clamp(g * 255 / maxVal, 0, 255);
                bgra[i++] = (byte)Math.Clamp(r * 255 / maxVal, 0, 255);
                bgra[i++] = 255;
            }

        return Build(w, h, bgra);
    }

    private static SKBitmap? ReadBinaryBitmap(byte[] data, int pos, int w, int h, byte[] bgra)
    {
        var rowBytes = (w + 7) / 8;
        if (pos + rowBytes * h > data.Length) return null;
        var i = 0;
        for (var y = 0; y < h; y++)
        {
            var rowStart = pos + y * rowBytes;
            for (var x = 0; x < w; x++)
            {
                var bit = data[rowStart + (x >> 3)] >> 7 - (x & 7) & 1;
                var c = bit == 0 ? (byte)255 : (byte)0;
                bgra[i++] = c;
                bgra[i++] = c;
                bgra[i++] = c;
                bgra[i++] = 255;
            }
        }

        return Build(w, h, bgra);
    }

    private static SKBitmap? ReadBinaryGray(byte[] data, int pos, int w, int h, int maxVal, byte[] bgra)
    {
        var two = maxVal > 255;
        var need = (long)w * h * (two ? 2 : 1);
        if (pos + need > data.Length) return null;
        var i = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                int v;
                if (two)
                {
                    v = data[pos] << 8 | data[pos + 1];
                    pos += 2;
                }
                else
                {
                    v = data[pos++];
                }

                var c = (byte)Math.Clamp(v * 255 / maxVal, 0, 255);
                bgra[i++] = c;
                bgra[i++] = c;
                bgra[i++] = c;
                bgra[i++] = 255;
            }

        return Build(w, h, bgra);
    }

    private static SKBitmap? ReadBinaryRgb(byte[] data, int pos, int w, int h, int maxVal, byte[] bgra)
    {
        var two = maxVal > 255;
        var need = (long)w * h * 3 * (two ? 2 : 1);
        if (pos + need > data.Length) return null;
        var i = 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                int r, g, b;
                if (two)
                {
                    r = data[pos] << 8 | data[pos + 1];
                    g = data[pos + 2] << 8 | data[pos + 3];
                    b = data[pos + 4] << 8 | data[pos + 5];
                    pos += 6;
                }
                else
                {
                    r = data[pos++];
                    g = data[pos++];
                    b = data[pos++];
                }

                bgra[i++] = (byte)Math.Clamp(b * 255 / maxVal, 0, 255);
                bgra[i++] = (byte)Math.Clamp(g * 255 / maxVal, 0, 255);
                bgra[i++] = (byte)Math.Clamp(r * 255 / maxVal, 0, 255);
                bgra[i++] = 255;
            }

        return Build(w, h, bgra);
    }

    private static bool TryReadInt(byte[] data, ref int pos, out int value)
    {
        value = 0;
        SkipWhitespaceAndComments(data, ref pos);
        if (pos >= data.Length) return false;
        var sb = new StringBuilder();
        while (pos < data.Length && !IsWhitespace(data[pos]))
        {
            sb.Append((char)data[pos]);
            pos++;
        }

        return int.TryParse(sb.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void SkipWhitespaceAndComments(byte[] data, ref int pos)
    {
        while (pos < data.Length)
        {
            if (IsWhitespace(data[pos]))
            {
                pos++;
                continue;
            }

            if (data[pos] == '#')
            {
                while (pos < data.Length && data[pos] != '\n' && data[pos] != '\r')
                    pos++;
                continue;
            }

            break;
        }
    }

    private static bool IsWhitespace(byte b)
    {
        return b is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r';
    }
}