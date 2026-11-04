using SkiaSharp;

namespace ImageToIcon.Services;

// .dib/.rle files are usually complete BMPs with the "BM" file header — those go straight
// to Skia. Headerless DIBs (BITMAPINFOHEADER + pixel data only) get a synthetic file header
// prepended before handing off.
public static class DibDecoder
{
    public static SKBitmap? TryDecode(byte[] data)
    {
        if (data.Length < 4) return null;

        if (data[0] == 'B' && data[1] == 'M')
            return SKBitmap.Decode(data);

        var biSize = BitConverter.ToUInt32(data, 0);
        if (biSize is < 12 or > 128) return null;

        int bitCount;
        uint clrUsed = 0;
        int paletteEntrySize;

        if (biSize == 12)
        {
            // BITMAPCOREHEADER: biBitCount at offset 10, 3-byte palette entries.
            if (data.Length < 12) return null;
            bitCount = BitConverter.ToUInt16(data, 10);
            paletteEntrySize = 3;
        }
        else
        {
            // BITMAPINFOHEADER (40) or variants: biBitCount at 14, biClrUsed at 32.
            if (data.Length < 40) return null;
            bitCount = BitConverter.ToUInt16(data, 14);
            clrUsed = BitConverter.ToUInt32(data, 32);
            paletteEntrySize = 4;
        }

        var paletteCount = clrUsed;
        if (paletteCount == 0 && bitCount <= 8)
            paletteCount = (uint)(1 << bitCount);

        var paletteBytes = paletteCount * (uint)paletteEntrySize;
        var offBits = 14 + biSize + paletteBytes;
        var totalSize = 14 + (uint)data.Length;

        var wrapped = new byte[14 + data.Length];
        wrapped[0] = (byte)'B';
        wrapped[1] = (byte)'M';
        WriteUInt32Le(wrapped, 2, totalSize);
        WriteUInt32Le(wrapped, 10, offBits);
        Buffer.BlockCopy(data, 0, wrapped, 14, data.Length);

        return SKBitmap.Decode(wrapped);
    }

    private static void WriteUInt32Le(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)(value >> 8 & 0xFF);
        buf[offset + 2] = (byte)(value >> 16 & 0xFF);
        buf[offset + 3] = (byte)(value >> 24 & 0xFF);
    }
}