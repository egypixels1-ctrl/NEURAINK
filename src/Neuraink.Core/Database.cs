using System.Text.Json;

namespace Neuraink.Core;

/// <summary>
/// The NEURAINK Database folder (chosen in Settings, like the entity Database folder of DEEPMATH): everything the program
/// learns from and produces lives in it, so it can be moved, backed up or opened on another computer.
/// <code>
/// NEURAINK Database\
///   Artboards\           handwritten pieces: 900001.json (text, size, reference picture) + 900001.png (white)
///   Library\ديواني\      report.csv, clean_png\, svg\, thumbs\   (made by tools\diwani_triage.py, one folder per script style)
///   Annotations\          00042.json  (the segmentation of piece 42)
///   Letters\0645\         00042_01.png + .json  (letter cut-outs: the training data)
///   Models\               trained neural networks
///   Engine\               the AI engine
///   .neuraink\            database.json
/// </code>
/// </summary>
public sealed class NeurainkDatabase
{
    public static readonly string[] Folders = ["Library", "Artboards", "Annotations", "Letters", "Models", "Engine", ".neuraink"];

    public string Root { get; }
    public NeurainkDatabase(string root) => Root = (root ?? "").Trim();

    public bool IsSet => Root.Length > 0;
    public bool Exists => IsSet && Directory.Exists(Root);
    public string LibraryRoot => Path.Combine(Root, "Library");
    public string AnnotationsFolder => Path.Combine(Root, "Annotations");
    public string LettersFolder => Path.Combine(Root, "Letters");
    public string ModelsFolder => Path.Combine(Root, "Models");
    public string ArtboardsFolder => Path.Combine(Root, "Artboards");
    public string EngineFolder => Path.Combine(Root, "Engine");
    private string InfoFile => Path.Combine(Root, ".neuraink", "database.json");

    /// <summary>The parts this folder lacks to be a complete database (relative names).</summary>
    public List<string> Missing()
    {
        if (!Exists) return [.. Folders];
        var list = Folders.Where(f => !Directory.Exists(Path.Combine(Root, f))).Select(f => f + "\\").ToList();
        if (!File.Exists(InfoFile)) list.Add(@".neuraink\database.json");
        return list;
    }

    public bool IsComplete => Exists && Missing().Count == 0;

    /// <summary>Creates only the missing folders and files (nothing is replaced).</summary>
    public void CreateMissing()
    {
        Directory.CreateDirectory(Root);
        foreach (var f in Folders) Directory.CreateDirectory(Path.Combine(Root, f));
        try { File.SetAttributes(Path.Combine(Root, ".neuraink"), FileAttributes.Directory | FileAttributes.Hidden); } catch { }
        if (!File.Exists(InfoFile))
            File.WriteAllText(InfoFile, JsonSerializer.Serialize(new { app = "NEURAINK", format = 1, created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The script styles of the library: the folders of Library\ that contain a report.csv.</summary>
    public List<string> Styles()
    {
        var list = new List<string>();
        try
        {
            if (Directory.Exists(LibraryRoot))
                list.AddRange(Directory.EnumerateDirectories(LibraryRoot)
                    .Where(d => File.Exists(Path.Combine(d, "report.csv")))
                    .Select(d => Path.GetFileName(d)!));
        }
        catch { }
        return list;
    }

    /// <summary>
    /// The library folder of a style: Library\&lt;style&gt;, else the first style found in Library\, else (older layout)
    /// a folder next to Library\ that contains a report.csv (e.g. "diwani" made before the database existed).
    /// </summary>
    public string LibraryFolder(string style)
    {
        if (!Exists) return "";
        var own = Path.Combine(LibraryRoot, style ?? "");
        if (!string.IsNullOrWhiteSpace(style) && File.Exists(Path.Combine(own, "report.csv"))) return own;
        var first = Styles().FirstOrDefault();
        if (first != null) return Path.Combine(LibraryRoot, first);
        if (File.Exists(Path.Combine(Root, "report.csv"))) return Root;
        try
        {
            var legacy = Directory.EnumerateDirectories(Root).FirstOrDefault(d => File.Exists(Path.Combine(d, "report.csv")));
            if (legacy != null) return legacy;
        }
        catch { }
        return own;
    }
}
