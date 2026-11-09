using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageToIcon.Services;

public class Settings
{
    public static readonly int[] DefaultIcnsSizes = [1024, 512, 256, 128, 64, 32, 16];

    public int[] SelectedSizes { get; set; } = IconFactory.DefaultSizes.ToArray();
    public int[] CustomSizes { get; set; } = [];
    public bool IcnsFormat { get; set; }
    public bool HicolorFormat { get; set; }
    public int[] IcnsSelectedSizes { get; set; } = DefaultIcnsSizes.ToArray();
    public int[] HicolorSelectedSizes { get; set; } = IconFactory.DefaultHicolorSizes.ToArray();
    public bool ShowThumbBorders { get; set; } = true;
    public double CornerRadius { get; set; } = 0.40;
    public double Padding { get; set; } = 0.05;
    public uint ShadowColor { get; set; } = 0xC8000000;
    public double ShadowOffsetX { get; set; }
    public double ShadowOffsetY { get; set; } = 0.02;
    public double ShadowBlur { get; set; } = 0.01;
    public uint StrokeColor { get; set; } = 0x80000000;
    public double StrokeWidth { get; set; } = 0.01;
    public double StrokeOffset { get; set; }
    public DateTime LastUpdateCheckUtc { get; set; }
    public string PendingUpdateVersion { get; set; } = "";

    private static string ConfigPath
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            if (Environment.ProcessPath is { } exe)
                dir = Path.GetDirectoryName(exe) ?? dir;
            return Path.Combine(dir, "settings.json");
        }
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
                return JsonSerializer.Deserialize(File.ReadAllText(ConfigPath), SettingsJsonContext.Default.Settings) ?? new Settings();
        }
        catch
        {
            // ignored
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            var def = new Settings();
            using var stream = File.Create(ConfigPath);
            using var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            w.WriteStartObject();

            if (!SelectedSizes.SequenceEqual(def.SelectedSizes))
            {
                w.WriteStartArray(nameof(SelectedSizes));
                foreach (var v in SelectedSizes) w.WriteNumberValue(v);
                w.WriteEndArray();
            }

            if (!CustomSizes.SequenceEqual(def.CustomSizes))
            {
                w.WriteStartArray(nameof(CustomSizes));
                foreach (var v in CustomSizes) w.WriteNumberValue(v);
                w.WriteEndArray();
            }

            if (IcnsFormat != def.IcnsFormat)
                w.WriteBoolean(nameof(IcnsFormat), IcnsFormat);

            if (HicolorFormat != def.HicolorFormat)
                w.WriteBoolean(nameof(HicolorFormat), HicolorFormat);

            if (!IcnsSelectedSizes.SequenceEqual(def.IcnsSelectedSizes))
            {
                w.WriteStartArray(nameof(IcnsSelectedSizes));
                foreach (var v in IcnsSelectedSizes) w.WriteNumberValue(v);
                w.WriteEndArray();
            }

            if (!HicolorSelectedSizes.SequenceEqual(def.HicolorSelectedSizes))
            {
                w.WriteStartArray(nameof(HicolorSelectedSizes));
                foreach (var v in HicolorSelectedSizes) w.WriteNumberValue(v);
                w.WriteEndArray();
            }

            if (ShowThumbBorders != def.ShowThumbBorders)
                w.WriteBoolean(nameof(ShowThumbBorders), ShowThumbBorders);

            WriteDouble(w, nameof(CornerRadius), CornerRadius, def.CornerRadius);
            WriteDouble(w, nameof(Padding), Padding, def.Padding, 3);
            if (ShadowColor != def.ShadowColor)
                w.WriteNumber(nameof(ShadowColor), ShadowColor);
            WriteDouble(w, nameof(ShadowOffsetX), ShadowOffsetX, def.ShadowOffsetX);
            WriteDouble(w, nameof(ShadowOffsetY), ShadowOffsetY, def.ShadowOffsetY);
            WriteDouble(w, nameof(ShadowBlur), ShadowBlur, def.ShadowBlur);
            if (StrokeColor != def.StrokeColor)
                w.WriteNumber(nameof(StrokeColor), StrokeColor);
            WriteDouble(w, nameof(StrokeWidth), StrokeWidth, def.StrokeWidth);
            WriteDouble(w, nameof(StrokeOffset), StrokeOffset, def.StrokeOffset);

            if (LastUpdateCheckUtc != def.LastUpdateCheckUtc)
                w.WriteString(nameof(LastUpdateCheckUtc), LastUpdateCheckUtc);
            if (PendingUpdateVersion != def.PendingUpdateVersion)
                w.WriteString(nameof(PendingUpdateVersion), PendingUpdateVersion);

            w.WriteEndObject();
        }
        catch
        {
            // ignored
        }
    }

    private static void WriteDouble(Utf8JsonWriter w, string name, double value, double defaultValue, int decimals = 2)
    {
        var rounded = Math.Round(value, decimals);
        if (Math.Abs(rounded - Math.Round(defaultValue, decimals)) < 1e-9)
            return;
        w.WriteNumber(name, rounded);
    }
}

[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext;