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

    /// <summary>Folder made by the triage tool (contains report.csv, clean_png, svg, thumbs).</summary>
    public string LibraryPath { get; set; } = "";
    /// <summary>Workspace: annotations and the letter library.</summary>
    public string DataPath { get; set; } = "";
    /// <summary>Script style recorded with every segmented letter.</summary>
    public string Style { get; set; } = "ديواني";
    /// <summary>Show a small picture of the original while segmenting.</summary>
    public bool ShowOriginal { get; set; } = true;

    /// <summary>Fills empty paths with sensible defaults (the D:\CalligraphyAI layout when it exists).</summary>
    public void FillDefaults()
    {
        if (string.IsNullOrWhiteSpace(LibraryPath))
        {
            var guess = @"D:\CalligraphyAI\diwani";
            if (Directory.Exists(guess)) LibraryPath = guess;
        }
        if (string.IsNullOrWhiteSpace(DataPath))
        {
            var root = !string.IsNullOrWhiteSpace(LibraryPath) && Directory.GetParent(LibraryPath) is { } p
                ? p.FullName
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            DataPath = Path.Combine(root, "NEURAINK Data");
        }
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
