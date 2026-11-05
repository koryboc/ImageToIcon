using System.Buffers.Binary;
using SkiaSharp;

namespace ImageToIcon.Services;

// Reads Apple .icns and returns the largest PNG-based frame. Older JPEG2000/raw chunks are skipped.
public static class IcnsReader
{
    public static readonly Dictionary<string, string[]> Categories = new()
    {
        ["Apple Icon Image"] = [".icns"]
    };

    public static readonly string[] SupportedExtensions = Categories.Values.SelectMany(v => v).ToArray();

    public static bool IsSupported(string path)
    {
        return SupportedExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    public static SKBitmap? TryLoad(string path)
    {
        try
        {
            return !File.Exists(path) ? null : DecodeLargestFrame(File.ReadAllBytes(path));
        }
        catch
        {
            return null;
        }
    }

    private static SKBitmap? DecodeLargestFrame(byte[] data)
    {
        if (data.Length < 8)
            return null;
        if (data[0] != 'i' || data[1] != 'c' || data[2] != 'n' || data[3] != 's')
            return null;

        var total = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4));
        if (total > data.Length)
            total = (uint)data.Length;

        SKBitmap? best = null;
        var pos = 8;
        while (pos + 8 <= total)
        {
            var len = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos + 4, 4));
            if (len < 8 || pos + len > total)
                break;

            var payload = data.AsSpan(pos + 8, (int)len - 8);
            if (LooksLikePng(payload))
            {
                var decoded = SKBitmap.Decode(payload);
                if (decoded != null && (best == null || decoded.Width > best.Width))
                {
                    best?.Dispose();
                    best = decoded;
                }
                else
                {
                    decoded?.Dispose();
                }
            }

            pos += (int)len;
        }

        return best;
    }

    private static bool LooksLikePng(ReadOnlySpan<byte> data)
    {
        return data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
    }
}
