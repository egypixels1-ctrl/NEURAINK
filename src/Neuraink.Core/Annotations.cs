using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neuraink.Core;

/// <summary>A calligraphic brush tip (like an Illustrator calligraphic brush): size in picture pixels, angle in degrees, roundness 0–1.</summary>
public sealed class BrushTip
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public double Size { get; set; } = 24;
    [JsonPropertyName("angle")] public double Angle { get; set; } = 70;
    [JsonPropertyName("roundness")] public double Roundness { get; set; } = 0.2;
    public BrushTip Copy() => new() { Name = Name, Size = Size, Angle = Angle, Roundness = Roundness };
}

/// <summary>A region drawn on a piece: a letter body, its dots, or an ornament / frame / signature to exclude.</summary>
public sealed class Segment
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "letter";   // letter | dots | exclude | stroke | ornament
    [JsonPropertyName("chip")] public int Chip { get; set; } = -1;            // index of the letter in the text
    [JsonPropertyName("letter")] public string Letter { get; set; } = "";
    [JsonPropertyName("form")] public string Form { get; set; } = "";
    [JsonPropertyName("points")] public List<double[]> Points { get; set; } = [];
    /// <summary>The pen path (Illustrator style): per anchor x, y, in-handle x, y, out-handle x, y. Empty for older regions.</summary>
    [JsonPropertyName("bezier")] public List<double[]>? Bezier { get; set; }
    /// <summary>Brush strokes (kind stroke / ornament): the tip and the path as x, y, pressure — in the order they were drawn.</summary>
    [JsonPropertyName("brush")] public BrushTip? Brush { get; set; }
    [JsonPropertyName("stroke")] public List<double[]>? Stroke { get; set; }
    [JsonPropertyName("entry")] public double[]? Entry { get; set; }
    [JsonPropertyName("exit")] public double[]? Exit { get; set; }
    [JsonPropertyName("entrySkipped")] public bool EntrySkipped { get; set; }
    [JsonPropertyName("exitSkipped")] public bool ExitSkipped { get; set; }
}

/// <summary>The segmentation of one piece (annotations\00042.json). Same format as the first browser version.</summary>
public sealed class Annotation
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("file")] public string File { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("state")] public string State { get; set; } = "";       // done | partial | rejected
    [JsonPropertyName("segments")] public List<Segment> Segments { get; set; } = [];
    [JsonPropertyName("savedAt")] public string SavedAt { get; set; } = "";
}

public sealed class AnnotationStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Folder { get; }
    private readonly Dictionary<int, string> _states = [];

    public AnnotationStore(string folder)
    {
        Folder = folder;
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var f in Directory.EnumerateFiles(Folder, "*.json"))
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(f), out var id)) continue;
                var a = Read(f);
                if (a != null) _states[id] = string.IsNullOrEmpty(a.State) ? "done" : a.State;
            }
        }
        catch { }
    }

    private string PathOf(int id) => Path.Combine(Folder, $"{id:00000}.json");

    /// <summary>"" (not segmented), done, partial or rejected.</summary>
    public string State(int id) => _states.GetValueOrDefault(id, "");
    public bool IsSegmented(int id) => State(id) is "done" or "partial";
    public int CountSegmented => _states.Values.Count(s => s is "done" or "partial");
    public int CountRejected => _states.Values.Count(s => s == "rejected");

    public Annotation? Load(int id) => Read(PathOf(id));

    private static Annotation? Read(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Annotation>(File.ReadAllText(path), Json) : null; }
        catch { return null; }
    }

    public void Save(Annotation a)
    {
        Directory.CreateDirectory(Folder);
        a.SavedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var path = PathOf(a.Id);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(a, Json));
        File.Move(tmp, path, overwrite: true);
        _states[a.Id] = a.State;
    }

    public void Delete(int id)
    {
        try { File.Delete(PathOf(id)); } catch { }
        _states.Remove(id);
    }
}
