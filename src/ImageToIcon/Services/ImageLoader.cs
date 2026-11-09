using Avalonia.Platform.Storage;
using SkiaSharp;

namespace ImageToIcon.Services;

public static class ImageLoader
{
    public static readonly Dictionary<string, string[]> RasterCategories = new()
    {
        ["Bitmap Image"] = [".bmp", ".dib", ".rle"],
        ["GIF Image"] = [".gif"],
        ["JPEG Image"] = [".jpg", ".jpeg", ".jpe", ".jfif"],
        ["PNG Image"] = [".png"],
        ["Portable Anymap"] = [".pbm", ".pgm", ".ppm", ".pnm"],
        ["Quite OK Image"] = [".qoi"],
        ["Targa Image"] = [".tga"],
        ["TIFF Image"] = [".tif", ".tiff"],
        ["WebP Image"] = [".webp"]
    };

    public static readonly Dictionary<string, string[]> VectorCategories = new()
    {
        ["SVG Image"] = SvgLoader.Extensions
    };

    public static readonly Dictionary<string, string[]> Categories =
        RasterCategories.Concat(VectorCategories)
                        .Concat(IconReader.Categories)
                        .Concat(IcnsReader.Categories)
                        .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

    public static readonly string[] SupportedExtensions =
        Categories.Values.SelectMany(v => v).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool IsSupported(string path)
    {
        return SupportedExtensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));
    }

    // Builds file picker filters: "All supported" first (preselected), then one entry per
    // category, and finally the OS "All files" option.
    public static IReadOnlyList<FilePickerFileType> BuildFilePickerFilters()
    {
        var filters = new List<FilePickerFileType>
        {
            new("All supported")
            {
                Patterns = SupportedExtensions.Select(e => "*" + e).ToArray()
            }
        };
        foreach (var (label, exts) in Categories)
            filters.Add(new FilePickerFileType(label) { Patterns = exts.Select(e => "*" + e).ToArray() });
        filters.Add(FilePickerFileTypes.All);
        return filters;
    }

    public static SKBitmap? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            if (IconReader.IsSupported(path))
                return IconReader.TryLoad(path);
            if (IcnsReader.IsSupported(path))
                return IcnsReader.TryLoad(path);
            return SvgLoader.IsSupported(path) ? SvgLoader.TryLoad(path) : RasterDecoder.TryLoad(path);
        }
        catch
        {
            return null;
        }
    }
}