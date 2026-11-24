using System.Text.Json;
using System.Text.Json.Serialization;

namespace Neuraink.Core;

/// <summary>A white artboard the calligrapher writes on directly (optionally over a reference picture).</summary>
public sealed class Artboard
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; } = 2000;
    [JsonPropertyName("height")] public int Height { get; set; } = 2000;
    [JsonPropertyName("created")] public string Created { get; set; } = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    /// <summary>Reference picture (to trace over; never part of the data or the output).</summary>
    [JsonPropertyName("reference")] public string? Reference { get; set; }
    [JsonPropertyName("ref_x")] public double RefX { get; set; }
    [JsonPropertyName("ref_y")] public double RefY { get; set; }
    [JsonPropertyName("ref_scale")] public double RefScale { get; set; } = 1;
    [JsonPropertyName("ref_opacity")] public double RefOpacity { get; set; } = 0.35;
}

/// <summary>Database\Artboards: one .json per artboard and its white .png (the size of the page).</summary>
public sealed class ArtboardStore
{
    /// <summary>Artboard ids start here, far from the library ids.</summary>
    public const int FirstId = 900001;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string Folder { get; }
    public ArtboardStore(string folder) => Folder = folder;

    public string JsonPath(int id) => Path.Combine(Folder, $"{id}.json");
    public string PngPath(int id) => Path.Combine(Folder, $"{id}.png");

    public List<Artboard> All()
    {
        var list = new List<Artboard>();
        try
        {
            if (!Directory.Exists(Folder)) return list;
            foreach (var f in Directory.EnumerateFiles(Folder, "*.json"))
                if (Load(f) is { } a) list.Add(a);
        }
        catch { }
        return list.OrderByDescending(a => a.Id).ToList();
    }

    public Artboard? Get(int id) => Load(JsonPath(id));

    private static Artboard? Load(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<Artboard>(File.ReadAllText(path), Json) : null; }
        catch { return null; }
    }

    public int NextId()
    {
        var ids = All().Select(a => a.Id).ToList();
        return ids.Count == 0 ? FirstId : ids.Max() + 1;
    }

    public void Save(Artboard a)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(JsonPath(a.Id), JsonSerializer.Serialize(a, Json));
    }

    /// <summary>The artboard as a piece the segmentation can open.</summary>
    public LibraryItem ToItem(Artboard a) => new()
    {
        Id = a.Id, Text = a.Text, File = $"Artboards\\{a.Id}", Status = "artboard", Source = "artboard",
        Png = PngPath(a.Id), Thumb = "", Freq = 1,
    };
}
