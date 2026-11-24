using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neuraink.Core;

public enum AppLanguage { English, Arabic }
public enum AppTheme { Dark, Light }

/// <summary>Program settings (%APPDATA%\NEURAINK\settings.json).</summary>
public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppLanguage Language { get; set; } = AppLanguage.Arabic;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Accent colour: "auto" (Windows accent) or #RRGGBB. NEURAINK violet by default.</summary>
    public string Accent { get; set; } = "#7C5CFF";
    /// <summary>Last picked accent colours (newest first, up to 6).</summary>
    public List<string> RecentAccents { get; set; } = [];
    /// <summary>Last colours picked in the colour window (newest first).</summary>
    public List<string> RecentColors { get; set; } = [];
    /// <summary>Settings › Appearance: the accent colour part is open.</summary>
    public bool AppearanceOpen { get; set; } = true;

    /// <summary>The NEURAINK Database folder (library, annotations, letters, models, engine).</summary>
    public string DatabasePath { get; set; } = "";
    /// <summary>Older versions: the library and workspace folders (read once to fill DatabasePath).</summary>
    public string? LibraryPath { get; set; }
    public string? DataPath { get; set; }
    /// <summary>Script style recorded with every segmented letter.</summary>
    public string Style { get; set; } = "ديواني";
    /// <summary>Show a small picture of the original while segmenting.</summary>
    public bool ShowOriginal { get; set; } = true;

    /// <summary>Settings of older versions: the folder that held the library becomes the database folder.</summary>
    public void FillDefaults()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath) && !string.IsNullOrWhiteSpace(LibraryPath) && Directory.GetParent(LibraryPath) is { } p)
            DatabasePath = p.FullName;
        LibraryPath = null;
        DataPath = null;
    }
}

public static class SettingsStore
{
    public static string Folder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NEURAINK");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        AppSettings s = new();
        try
        {
            if (File.Exists(FilePath))
                s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch { /* corrupt settings: start fresh */ }
        s.FillDefaults();
        return s;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
