using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageToIcon.Services;

public class Settings
{
    public int[] SelectedSizes { get; set; } = IconFactory.DefaultSizes.ToArray();
    public int[] CustomSizes { get; set; } = [];
    public bool ShowThumbBorders { get; set; } = true;
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
            File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, SettingsJsonContext.Default.Settings));
        }
        catch
        {
            // ignored
        }
    }
}

[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class SettingsJsonContext : JsonSerializerContext;