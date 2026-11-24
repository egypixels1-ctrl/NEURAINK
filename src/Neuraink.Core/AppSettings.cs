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
    /// <summary>Older versions: one brush (replaced by <see cref="Brushes"/>).</summary>
    public BrushTip? Brush { get; set; }
    /// <summary>Segmentation › Brushes: the user's calligraphic brushes (like the Illustrator Brushes panel).</summary>
    public List<BrushTip> Brushes { get; set; } = [];
    /// <summary>Index of the brush in use.</summary>
    public int BrushIndex { get; set; }
    /// <summary>Brush smoothing, like the Illustrator Paintbrush Fidelity: 0 accurate … 4 smooth.</summary>
    public int Fidelity { get; set; } = 4;
    /// <summary>Segmentation: after a brush stroke, move on to the next letter (Shift keeps the same letter).</summary>
    public bool AutoNextLetter { get; set; } = true;
    /// <summary>Segmentation: opacity of the reference picture (0.05–1), to trace over it.</summary>
    public double PictureOpacity { get; set; } = 1;
    /// <summary>Show a small picture of the original while segmenting.</summary>
    public bool ShowOriginal { get; set; } = true;

    /// <summary>Settings of older versions: the folder that held the library becomes the database folder.</summary>
    public void FillDefaults()
    {
        if (string.IsNullOrWhiteSpace(DatabasePath) && !string.IsNullOrWhiteSpace(LibraryPath) && Directory.GetParent(LibraryPath) is { } p)
            DatabasePath = p.FullName;
        LibraryPath = null;
        DataPath = null;
        if (Brushes.Count == 0) { Brushes = BrushTip.Defaults(); BrushIndex = 6; }   // start on the 70° calligraphic nib
        BrushIndex = Math.Clamp(BrushIndex, 0, Brushes.Count - 1);
        // the angle is shown next to the icon now: the starting names lose their "75°"
        foreach (var b in Brushes)
            if (System.Text.RegularExpressions.Regex.IsMatch(b.Name ?? "", @"^Calligraphic \d+°$")) b.Name = "Calligraphic";
        Brush = null;
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
