using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neuraink.Core;

/// <summary>A region drawn on a piece: a letter body, its dots, or an ornament / frame / signature to exclude.</summary>
public sealed class Segment
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "letter";   // letter | dots | exclude
    [JsonPropertyName("chip")] public int Chip { get; set; } = -1;            // index of the letter in the text
    [JsonPropertyName("letter")] public string Letter { get; set; } = "";
    [JsonPropertyName("form")] public string Form { get; set; } = "";
    [JsonPropertyName("points")] public List<double[]> Points { get; set; } = [];
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

    public AnnotationStore(string dataPath)
    {
        Folder = Path.Combine(dataPath, "annotations");
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
