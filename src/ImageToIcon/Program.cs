using System.Globalization;
using Avalonia;
using ImageToIcon.Services;
using ImageToIcon.Views;
using SkiaSharp;

namespace ImageToIcon;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Any(a => a is "--cli" or "--help"))
            return RunCli(args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
                         .UsePlatformDetect()
                         .WithInterFont()
                         .LogToTrace();
    }

    private static int RunCli(string[] args)
    {
        if (args.Any(a => a is "--help"))
        {
            PrintHelp();
            return 0;
        }

        var outDir = GetNamed(args, "o");
        if (string.IsNullOrWhiteSpace(outDir))
        {
            Console.WriteLine("Error: Missing output path.");
            PrintHelp();
            return 1;
        }

        outDir = outDir.Trim('"');
        if (!Directory.Exists(outDir))
        {
            Console.WriteLine($"Error: Output path '{outDir}' does not exist.");
            return 1;
        }

        var format = ParseFormat(GetNamed(args, "format"));
        var hicolorArchive = HasFlag(args, "hicolor-archive");
        var sizes = ParseSizes(args) ?? DefaultSizesFor(format);

        if (!TryParseDouble(GetNamed(args, "corner"), 0, 1, out var corner))
            return Fail("Invalid --corner value (0..1).");
        if (!TryParseDouble(GetNamed(args, "padding"), 0, 1, out var padding))
            return Fail("Invalid --padding value (0..1).");

        var fit = ParseFit(GetNamed(args, "fit"));
        if (fit == null)
            return Fail("Invalid --fit value (fit|stretch|crop).");

        if (!TryParseColor(GetNamed(args, "bg"), out var bg))
            return Fail("Invalid --bg color (#RRGGBB or #RRGGBBAA).");

        if (!TryParseShadow(GetNamed(args, "shadow"), out var shadow))
            return Fail("Invalid --shadow (expected color,offX,offY,blur).");
        if (!TryParseStroke(GetNamed(args, "stroke"), out var stroke))
            return Fail("Invalid --stroke (expected color,width,offset).");

        var shadowWithBg = bg.HasValue && !HasFlag(args, "shadow-no-bg");
        var strokeWithBg = bg.HasValue && !HasFlag(args, "stroke-no-bg");

        var inputs = args.Where(a => !IsSwitch(a) && ImageLoader.IsSupported(a)).ToList();
        if (inputs.Count == 0)
        {
            Console.WriteLine("No input images provided.");
            PrintHelp();
            return 1;
        }

        var nameOverride = GetNamed(args, "name")?.Trim().Trim('"');
        if (!string.IsNullOrEmpty(nameOverride) && inputs.Count > 1)
            return Fail("--name can only be used with a single input image.");

        foreach (var file in inputs)
        {
            if (!File.Exists(file))
            {
                Console.WriteLine($"Skipped: '{file}' not found.");
                continue;
            }

            Console.WriteLine($"Processing '{file}' ...");
            using var img = ImageLoader.TryLoad(file);
            if (img == null)
            {
                Console.WriteLine("Skipped: Invalid image format.");
                continue;
            }

            var frames = IconFactory.BuildSizes(img, sizes, corner, padding, fit.Value, 0, 0, 1, bg, shadow, stroke, shadowWithBg, strokeWithBg).ToList();
            try
            {
                var outPath = ResolveOutputPath(outDir, file, format, hicolorArchive, nameOverride);
                switch (format)
                {
                    case IconFormat.Ico:
                        IconFactory.Save(frames, outPath);
                        break;
                    case IconFormat.Icns:
                        IconFactory.SaveIcns(frames, outPath);
                        break;
                    case IconFormat.Hicolor:
                        var baseName = nameOverride ?? Path.GetFileNameWithoutExtension(file);
                        if (hicolorArchive)
                            IconFactory.SaveHicolorArchive(frames, outPath, baseName);
                        else
                            IconFactory.SaveHicolorFolder(frames, outPath, baseName);
                        break;
                }

                Console.WriteLine($"Done: '{outPath}' saved.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed: {ex.Message}");
            }
            finally
            {
                foreach (var f in frames) f.Dispose();
            }
        }

        return 0;
    }

    private static int Fail(string message)
    {
        Console.WriteLine($"Error: {message}");
        return 1;
    }

    private static string? GetNamed(string[] args, string name)
    {
        var prefix = "--" + name + "=";
        var arg = args.FirstOrDefault(a => a.StartsWith(prefix));
        return arg?[prefix.Length..];
    }

    private static bool HasFlag(string[] args, string name)
    {
        return args.Any(a => a == "--" + name);
    }

    private static int[]? ParseSizes(string[] args)
    {
        var raw = GetNamed(args, "sizes");
        if (raw == null) return null;
        raw = raw.Trim('"');
        var parsed = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(s => int.TryParse(s, out var n) ? n : -1)
                        .Where(n => n is >= IconFactory.MinSize and <= IconFactory.MaxSize)
                        .Distinct()
                        .ToArray();
        return parsed.Length > 0 ? parsed : null;
    }

    private static IconFormat ParseFormat(string? v)
    {
        return v?.Trim().ToLowerInvariant() switch
        {
            "icns" => IconFormat.Icns,
            "hicolor" => IconFormat.Hicolor,
            _ => IconFormat.Ico
        };
    }

    private static int[] DefaultSizesFor(IconFormat format)
    {
        return format switch
        {
            IconFormat.Hicolor => IconFactory.DefaultHicolorSizes.ToArray(),
            _ => IconFactory.DefaultSizes.ToArray()
        };
    }

    private static FitMode? ParseFit(string? v)
    {
        if (v == null) return FitMode.Fit;
        return v.Trim().ToLowerInvariant() switch
        {
            "fit" => FitMode.Fit,
            "stretch" => FitMode.Stretch,
            "crop" => FitMode.Crop,
            _ => null
        };
    }

    private static bool TryParseDouble(string? s, double min, double max, out double value)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            value = 0;
            return true;
        }

        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max)
            return true;
        value = 0;
        return false;
    }

    private static bool TryParseColor(string? s, out SKColor? color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(s))
            return true;
        var t = s.Trim().TrimStart('#');
        if (t.Length != 6 && t.Length != 8)
            return false;
        if (!uint.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return false;
        byte r, g, b, a;
        if (t.Length == 6)
        {
            r = (byte)(hex >> 16);
            g = (byte)(hex >> 8);
            b = (byte)hex;
            a = 255;
        }
        else
        {
            r = (byte)(hex >> 24);
            g = (byte)(hex >> 16);
            b = (byte)(hex >> 8);
            a = (byte)hex;
        }

        color = new SKColor(r, g, b, a);
        return true;
    }

    private static bool TryParseShadow(string? s, out ShadowSpec? shadow)
    {
        shadow = null;
        if (string.IsNullOrWhiteSpace(s))
            return true;
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4) return false;
        if (!TryParseColor(parts[0], out var c) || c == null) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var ox)) return false;
        if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var oy)) return false;
        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var blur) || blur < 0) return false;
        shadow = new ShadowSpec(c.Value, ox, oy, blur);
        return true;
    }

    private static bool TryParseStroke(string? s, out StrokeSpec? stroke)
    {
        stroke = null;
        if (string.IsNullOrWhiteSpace(s))
            return true;
        var parts = s.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is not (2 or 3)) return false;
        if (!TryParseColor(parts[0], out var c) || c == null) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || w < 0) return false;
        var off = 0.0;
        if (parts.Length == 3 && (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out off) || off is < 0 or > 1))
            return false;
        stroke = new StrokeSpec(c.Value, w, off);
        return true;
    }

    private static string ResolveOutputPath(string outDir, string inputFile, IconFormat format, bool hicolorArchive, string? nameOverride)
    {
        var name = nameOverride ?? Path.GetFileNameWithoutExtension(inputFile);
        var path = format switch
        {
            IconFormat.Icns => Path.Combine(outDir, name + ".icns"),
            IconFormat.Hicolor => hicolorArchive ? Path.Combine(outDir, name + ".tar.gz") : Path.Combine(outDir, name + "_hicolor"),
            _ => Path.Combine(outDir, name + ".ico")
        };
        if (format == IconFormat.Hicolor && !hicolorArchive)
            return path;
        if (File.Exists(path) || Directory.Exists(path))
        {
            var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            var ext = Path.GetExtension(path);
            var withoutExt = path[..^ext.Length];
            path = withoutExt + "___" + stamp + ext;
        }

        return path;
    }

    private static bool IsSwitch(string arg)
    {
        return arg.StartsWith("--");
    }

    private static void PrintHelp()
    {
        var name = Environment.ProcessPath is { } p ? Path.GetFileNameWithoutExtension(p) : "ImageToIcon";
        Console.WriteLine($"""
                           Usage: {name} [--cli] --o=<output-dir> [options] <image1> [image2 ...]

                             --cli                          Run in CLI mode.
                             --o=DIR                        Output directory.
                             --name=NAME                    Output base name (default: input file name). Single input only.
                             --format=ico|icns|hicolor      Output format (default: ico).
                             --hicolor-archive              Write hicolor as .tar.gz instead of folder.
                             --sizes=16,32,64               Comma-separated icon sizes.
                             --corner=0..1                  Corner radius (0 = square, 1 = full circle).
                             --padding=0..1                 Padding fraction.
                             --fit=fit|stretch|crop         Fit mode (default: fit).
                             --bg=#RRGGBB[AA]               Background color.
                             --shadow=#RGBA,offX,offY,blur  Shadow (offsets/blur as fraction of size).
                             --stroke=#RGBA,width[,offset]  Stroke (width fraction, offset 0..1).
                             --shadow-no-bg                 Shadow ignores background (only with --bg).
                             --stroke-no-bg                 Stroke ignores background (only with --bg).
                             --help                         Show this help.

                           Default ICO sizes:      {string.Join(", ", IconFactory.DefaultSizes)}
                           Default hicolor sizes:  {string.Join(", ", IconFactory.DefaultHicolorSizes)}
                           Size range:             {IconFactory.MinSize}-{IconFactory.MaxSize}
                           Supported input:        {string.Join(", ", ImageLoader.SupportedExtensions)}

                           Examples:
                             {name} --cli --o=./out logo.png
                             {name} --cli --o=./out --name=app-icon logo.png
                             {name} --cli --o=./out --sizes=16,32,48,256 logo.png
                             {name} --cli --o=./out --format=icns logo.png
                             {name} --cli --o=./out --format=hicolor --hicolor-archive logo.png
                             {name} --cli --o=./out --corner=0.4 --padding=0.05 logo.png
                             {name} --cli --o=./out --bg=#2E86DE --corner=1 logo.png
                             {name} --cli --o=./out --shadow=#000000C8,0,0.02,0.01 logo.png
                             {name} --cli --o=./out --stroke=#00000080,0.01,0.5 logo.png
                             {name} --cli --o=./out --bg=#FFFFFF --stroke=#000000FF,0.02 --stroke-no-bg logo.png
                             {name} --cli --o=./out --fit=crop logo.png
                           """);
    }
}
