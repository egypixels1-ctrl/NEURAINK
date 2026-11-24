using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neuraink.Core;

/// <summary>What is stored next to every letter cut-out (letters\0645\00042_01.json) — the training data of the neural networks.</summary>
public sealed class LetterMeta
{
    [JsonPropertyName("letter")] public string Letter { get; set; } = "";
    [JsonPropertyName("form")] public string Form { get; set; } = "";
    [JsonPropertyName("source_id")] public int SourceId { get; set; }
    [JsonPropertyName("source_text")] public string SourceText { get; set; } = "";
    [JsonPropertyName("source_file")] public string SourceFile { get; set; } = "";
    [JsonPropertyName("style")] public string Style { get; set; } = "";
    [JsonPropertyName("tier")] public string Tier { get; set; } = "";
    [JsonPropertyName("chip")] public int Chip { get; set; }
    /// <summary>Crop box in the clean image: left, top, right, bottom (pixels).</summary>
    [JsonPropertyName("bbox")] public int[] BBox { get; set; } = [];
    [JsonPropertyName("polygons")] public List<List<double[]>> Polygons { get; set; } = [];
    /// <summary>The pen paths of the regions (anchor + handles), when drawn with the pen tool.</summary>
    [JsonPropertyName("beziers")] public List<List<double[]>>? Beziers { get; set; }
    [JsonPropertyName("entry")] public double[]? Entry { get; set; }
    [JsonPropertyName("exit")] public double[]? Exit { get; set; }
    [JsonPropertyName("image_size")] public int[] ImageSize { get; set; } = [];
}

public sealed class LetterGroup
{
    public required string Letter { get; init; }
    public required string Code { get; init; }
    public int Count { get; init; }
    public Dictionary<string, int> Forms { get; init; } = [];
    /// <summary>Full paths of the cut-outs, newest first.</summary>
    public List<string> Samples { get; init; } = [];
}

public sealed class LetterLibrary
{
    public const int Target = 150;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Folder { get; }

    public LetterLibrary(string folder)
    {
        Folder = folder;
        try { Directory.CreateDirectory(Folder); } catch { }
    }

    /// <summary>Removes the cut-outs of one piece (before it is exported again).</summary>
    public void RemoveSource(int id)
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(Folder))
                foreach (var f in Directory.EnumerateFiles(dir, $"{id:00000}_*"))
                    File.Delete(f);
        }
        catch { }
    }

    public string PathFor(string letter, int id, int chip, string ext)
    {
        var dir = Path.Combine(Folder, Arabic.Code(letter));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{id:00000}_{Math.Max(0, chip):00}.{ext}");
    }

    public void WriteMeta(string pngPath, LetterMeta meta) =>
        File.WriteAllText(Path.ChangeExtension(pngPath, ".json"), JsonSerializer.Serialize(meta, Json));

    public List<LetterGroup> Groups()
    {
        var list = new List<LetterGroup>();
        if (!Directory.Exists(Folder)) return list;
        foreach (var dir in Directory.EnumerateDirectories(Folder))
        {
            var code = Path.GetFileName(dir);
            var letter = Arabic.FromCode(code);
            if (letter.Length == 0) continue;
            var pngs = new DirectoryInfo(dir).GetFiles("*.png").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            if (pngs.Count == 0) continue;
            var forms = new Dictionary<string, int>();
            foreach (var j in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    var m = JsonSerializer.Deserialize<LetterMeta>(File.ReadAllText(j), Json);
                    var f = m?.Form ?? "";
                    forms[f] = forms.GetValueOrDefault(f) + 1;
                }
                catch { }
            }
            list.Add(new LetterGroup { Letter = letter, Code = code, Count = pngs.Count, Forms = forms, Samples = pngs.Select(f => f.FullName).ToList() });
        }
        return list.OrderByDescending(g => g.Count).ThenBy(g => g.Letter).ToList();
    }
}
