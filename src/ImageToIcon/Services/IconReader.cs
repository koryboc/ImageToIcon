using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace ImageToIcon.Services;

// Reads .ico files and PE containers (.exe/.dll) and returns the largest embedded frame as an
// SKBitmap. PE structural navigation ported from Apportia's PeReader.
public static class IconReader
{
    public static readonly Dictionary<string, string[]> Categories = new()
    {
        ["Windows Icon"] = [".ico"],
        ["Windows Executable"] = [".exe"],
        ["Windows Library"] = [".dll"]
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
            if (!File.Exists(path)) return null;
            var ext = Path.GetExtension(path);
            var ico = ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) ? File.ReadAllBytes(path) : ExtractIcoFromPe(path);
            return ico == null ? null : DecodeLargestFrame(ico);
        }
        catch
        {
            return null;
        }
    }

    private static SKBitmap? DecodeLargestFrame(byte[] icoBytes)
    {
        using var ms = new MemoryStream(icoBytes);
        using var br = new BinaryReader(ms);
        if (br.ReadUInt16() != 0) return null;
        if (br.ReadUInt16() != 1) return null;
        int count = br.ReadUInt16();
        if (count == 0) return null;

        var entries = new (byte W, byte H, uint Size, uint Offset)[count];
        for (var i = 0; i < count; i++)
        {
            var w = br.ReadByte();
            var h = br.ReadByte();
            br.ReadBytes(6); // colorCount, reserved, planes, bitCount
            var size = br.ReadUInt32();
            var offset = br.ReadUInt32();
            entries[i] = (w, h, size, offset);
        }

        // Sort descending by width; 0 means 256 or larger.
        Array.Sort(entries, (a, b) => Dim(b.W).CompareTo(Dim(a.W)));

        foreach (var (w, h, size, offset) in entries)
        {
            ms.Seek(offset, SeekOrigin.Begin);
            var data = br.ReadBytes((int)size);
            if (data.Length == 0) continue;

            // PNG entries decode directly via Skia — no Avalonia round-trip needed.
            if (IsPng(data))
            {
                var decoded = SKBitmap.Decode(data);
                if (decoded != null) return decoded;
                continue;
            }

            // Classic DIB entries: wrap in a single-entry ICO and let Avalonia (SkiaSharp) decode
            // — same trick used in Apportia's PeReader. Handles 1/4/8/24/32 bpp with AND mask.
            var dib = TryDecodeDibViaAvalonia(data, w, h);
            if (dib != null) return dib;
        }

        return null;
    }

    private static SKBitmap? TryDecodeDibViaAvalonia(byte[] dibData, byte width, byte height)
    {
        try
        {
            using var ms = new MemoryStream(WrapDibInIco(dibData, width, height));
            using var bmp = new Bitmap(ms);
            return BitmapToSkia(bmp);
        }
        catch
        {
            return null;
        }
    }

    private static SKBitmap BitmapToSkia(Bitmap bmp)
    {
        var w = bmp.PixelSize.Width;
        var h = bmp.PixelSize.Height;
        var stride = w * 4;
        var buffer = new byte[stride * h];

        var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            bmp.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), buffer.Length, stride);
        }
        finally
        {
            handle.Free();
        }

        // Avalonia returns BGRA. If premultiplied, unpremultiply so downstream resize sampling
        // treats color as straight alpha.
        if (bmp.AlphaFormat == AlphaFormat.Premul)
        {
            for (var i = 0; i < buffer.Length; i += 4)
            {
                var a = buffer[i + 3];
                if (a is 0 or 255) continue;
                buffer[i] = (byte)Math.Min(255, buffer[i] * 255 / a);
                buffer[i + 1] = (byte)Math.Min(255, buffer[i + 1] * 255 / a);
                buffer[i + 2] = (byte)Math.Min(255, buffer[i + 2] * 255 / a);
            }
        }

        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var result = new SKBitmap(info);
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        if (!result.InstallPixels(info, pinned.AddrOfPinnedObject(), info.RowBytes, (_, _) => pinned.Free()))
        {
            pinned.Free();
            result.Dispose();
            throw new InvalidOperationException("Failed to install pixels on SKBitmap.");
        }

        return result;
    }

    private static int Dim(byte v)
    {
        return v == 0 ? 256 : v;
    }

    private static bool IsPng(byte[] data)
    {
        return data.Length > 4 && data[0] == 0x89 && data[1] == 0x50;
    }

    private static byte[] WrapDibInIco(byte[] dibData, byte width, byte height)
    {
        var ico = new byte[6 + 16 + dibData.Length];
        using var iw = new BinaryWriter(new MemoryStream(ico));
        iw.Write((ushort)0);
        iw.Write((ushort)1);
        iw.Write((ushort)1);
        iw.Write(width);
        iw.Write(height);
        iw.Write((ushort)0);
        iw.Write((ushort)1);
        iw.Write((ushort)0);
        iw.Write((uint)dibData.Length);
        iw.Write((uint)(6 + 16));
        Array.Copy(dibData, 0, ico, 6 + 16, dibData.Length);
        return ico;
    }

    // ── PE resource walking ──────────────────────────────────────────────────

    private static byte[]? ExtractIcoFromPe(string peFilePath)
    {
        using var fs = new FileStream(peFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var br = new BinaryReader(fs);

        if (!TryGetRsrcSection(br, fs, out var rsrcRaw, out var rsrcVa))
            return null;

        long RvaToFile(uint rva)
        {
            return rsrcRaw + (rva - rsrcVa);
        }

        var iconDataById = new Dictionary<uint, byte[]>();
        foreach (var leaf in CollectByType(br, fs, rsrcRaw, 3)) // RT_ICON
        {
            fs.Seek(RvaToFile(leaf.DataRva), SeekOrigin.Begin);
            iconDataById[leaf.Id] = br.ReadBytes((int)leaf.Size);
        }

        if (iconDataById.Count == 0) return null;

        foreach (var group in CollectByType(br, fs, rsrcRaw, 14)) // RT_GROUP_ICON
        {
            fs.Seek(RvaToFile(group.DataRva), SeekOrigin.Begin);
            var built = BuildIcoFromGroup(br.ReadBytes((int)group.Size), iconDataById);
            if (built != null) return built;
        }

        return null;
    }

    private static byte[]? BuildIcoFromGroup(byte[] groupData, Dictionary<uint, byte[]> iconDataById)
    {
        if (groupData.Length < 6) return null;
        using var gr = new BinaryReader(new MemoryStream(groupData));
        gr.ReadUInt16(); // reserved
        gr.ReadUInt16(); // type
        int count = gr.ReadUInt16();
        if (count == 0) return null;

        var entries = new List<(byte W, byte H, byte Colors, byte Reserved, ushort Planes, ushort Bits, byte[] Data)>(count);
        for (var i = 0; i < count; i++)
        {
            var w = gr.ReadByte();
            var h = gr.ReadByte();
            var colors = gr.ReadByte();
            var reserved = gr.ReadByte();
            var planes = gr.ReadUInt16();
            var bits = gr.ReadUInt16();
            gr.ReadUInt32(); // dwBytesInRes
            var id = (uint)gr.ReadUInt16();
            if (iconDataById.TryGetValue(id, out var data))
                entries.Add((w, h, colors, reserved, planes, bits, data));
        }

        if (entries.Count == 0) return null;

        var dirSize = 6 + 16 * entries.Count;
        var totalDataSize = entries.Sum(e => e.Data.Length);
        var buf = new byte[dirSize + totalDataSize];
        using var iw = new BinaryWriter(new MemoryStream(buf));
        iw.Write((ushort)0);
        iw.Write((ushort)1);
        iw.Write((ushort)entries.Count);

        var offset = (uint)dirSize;
        foreach (var (w, h, colors, reserved, planes, bits, data) in entries)
        {
            iw.Write(w);
            iw.Write(h);
            iw.Write(colors);
            iw.Write(reserved);
            iw.Write(planes);
            iw.Write(bits);
            iw.Write((uint)data.Length);
            iw.Write(offset);
            offset += (uint)data.Length;
        }

        var pos = dirSize;
        foreach (var e in entries)
        {
            Array.Copy(e.Data, 0, buf, pos, e.Data.Length);
            pos += e.Data.Length;
        }

        return buf;
    }

    private static bool TryGetRsrcSection(BinaryReader br, FileStream fs, out long rsrcRaw, out uint rsrcVa)
    {
        rsrcRaw = 0;
        rsrcVa = 0;
        if (br.ReadUInt16() != 0x5A4D) return false;
        fs.Seek(0x3C, SeekOrigin.Begin);
        long peOffset = br.ReadUInt32();

        fs.Seek(peOffset, SeekOrigin.Begin);
        if (br.ReadUInt32() != 0x00004550) return false;

        fs.Seek(peOffset + 6, SeekOrigin.Begin);
        var numSections = br.ReadUInt16();
        fs.Seek(peOffset + 20, SeekOrigin.Begin);
        var optHeaderSize = br.ReadUInt16();
        fs.Seek(peOffset + 24, SeekOrigin.Begin);
        var magic = br.ReadUInt16();
        if (magic != 0x10B && magic != 0x20B) return false;

        var ddBase = peOffset + 24 + (magic == 0x20B ? 112L : 96L);
        fs.Seek(ddBase + 16, SeekOrigin.Begin);
        var resourceRva = br.ReadUInt32();
        if (resourceRva == 0) return false;

        var sectionsStart = peOffset + 24 + optHeaderSize;
        for (var i = 0; i < numSections; i++)
        {
            fs.Seek(sectionsStart + i * 40L + 8, SeekOrigin.Begin);
            var vSize = br.ReadUInt32();
            var vAddr = br.ReadUInt32();
            br.ReadUInt32();
            var rawPtr = br.ReadUInt32();
            if (vAddr > resourceRva || resourceRva >= vAddr + vSize) continue;
            rsrcVa = vAddr;
            rsrcRaw = rawPtr;
            return true;
        }

        return false;
    }

    private static List<Leaf> CollectByType(BinaryReader br, FileStream fs, long rsrcBase, uint typeId)
    {
        fs.Seek(rsrcBase, SeekOrigin.Begin);
        ReadDirCounts(br, out var named, out var idCount);
        for (var i = 0; i < named + idCount; i++)
        {
            var n = br.ReadUInt32();
            var d = br.ReadUInt32();
            if ((n & 0x80000000) == 0 && (n & 0x7FFFFFFF) == typeId && (d & 0x80000000) != 0)
                return WalkSubdir(br, fs, rsrcBase, d & 0x7FFFFFFF, 0);
        }

        return [];
    }

    private static List<Leaf> WalkSubdir(BinaryReader br, FileStream fs, long rsrcBase, long dirOff, uint parentId)
    {
        var result = new List<Leaf>();
        fs.Seek(rsrcBase + dirOff, SeekOrigin.Begin);
        ReadDirCounts(br, out var named, out var idCount);

        var entries = new (uint Id, bool IsSubdir, uint Offset)[named + idCount];
        for (var i = 0; i < entries.Length; i++)
        {
            var n = br.ReadUInt32();
            var d = br.ReadUInt32();
            entries[i] = (n & 0x7FFFFFFF, (d & 0x80000000) != 0, d & 0x7FFFFFFF);
        }

        foreach (var (id, isSubdir, offset) in entries)
        {
            var myId = parentId != 0 ? parentId : id;
            if (isSubdir)
            {
                result.AddRange(WalkSubdir(br, fs, rsrcBase, offset, myId));
                continue;
            }

            fs.Seek(rsrcBase + offset, SeekOrigin.Begin);
            result.Add(new Leaf(myId, br.ReadUInt32(), br.ReadUInt32()));
        }

        return result;
    }

    private static void ReadDirCounts(BinaryReader br, out int named, out int id)
    {
        br.ReadUInt32();
        br.ReadUInt32();
        br.ReadUInt16();
        br.ReadUInt16();
        named = br.ReadUInt16();
        id = br.ReadUInt16();
    }

    private readonly record struct Leaf(uint Id, uint DataRva, uint Size);
}