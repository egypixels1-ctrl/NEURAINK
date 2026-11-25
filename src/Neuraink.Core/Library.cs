using System.Text;

namespace Neuraink.Core;

/// <summary>One usable piece of the library (a row of report.csv made by tools/diwani_triage.py).</summary>
public sealed class LibraryItem
{
    public int Id { get; init; }
    public string Text { get; set; } = "";
    public string File { get; init; } = "";
    public string Status { get; init; } = "";
    public string Tier { get; init; } = "";
    public string Png { get; init; } = "";
    public string Svg { get; init; } = "";
    public string Thumb { get; init; } = "";
    public int Freq { get; set; }
    /// <summary>library (a piece of the triage library) or artboard (written directly on a white artboard).</summary>
    public string Source { get; init; } = "library";
    public bool IsArtboard => Source == "artboard";
    public int Words => Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
}

/// <summary>Reads the library folder (report.csv + clean_png / svg / thumbs).</summary>
public sealed class Library
{
    public static readonly string[] UsableStatuses = ["clean", "needs_cleaning", "inverted"];

    public string Folder { get; }
    public IReadOnlyList<LibraryItem> Items { get; }
    public bool Found { get; }

    private readonly Dictionary<int, LibraryItem> _byId;

    public Library(string folder)
    {
        Folder = folder ?? "";
        var items = new List<LibraryItem>();
        var report = Path.Combine(Folder, "report.csv");
        Found = System.IO.File.Exists(report);
        if (Found)
        {
            try { items = Parse(System.IO.File.ReadAllText(report, Encoding.UTF8)); }
            catch { Found = false; }
        }
        var freq = items.GroupBy(i => i.Text).ToDictionary(g => g.Key, g => g.Count());
        foreach (var i in items) i.Freq = freq[i.Text];
        Items = items;
        _byId = items.GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());
    }

    public LibraryItem? Get(int id) => _byId.GetValueOrDefault(id);

    public string FullPath(string relative) =>
        string.IsNullOrEmpty(relative) ? "" : Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));

    public static List<LibraryItem> Parse(string csv)
    {
        var rows = Csv.Read(csv);
        if (rows.Count == 0) return [];
        var head = rows[0].Select((h, i) => (h.Trim().TrimStart('﻿'), i)).ToDictionary(x => x.Item1, x => x.i);
        string Get(string[] r, string k) => head.TryGetValue(k, out var i) && i < r.Length ? r[i] : "";
        var list = new List<LibraryItem>();
        foreach (var r in rows.Skip(1))
        {
            var status = Get(r, "status");
            var png = Get(r, "clean_png");
            if (!UsableStatuses.Contains(status) || string.IsNullOrWhiteSpace(png)) continue;
            if (!int.TryParse(Get(r, "id"), out var id)) continue;
            list.Add(new LibraryItem
            {
                Id = id, Text = Get(r, "text").Trim(), File = Get(r, "file"), Status = status, Tier = Get(r, "tier"),
                Png = png.Replace('\\', '/'), Svg = Get(r, "svg").Replace('\\', '/'), Thumb = Get(r, "thumb").Replace('\\', '/'),
            });
        }
        return list;
    }
}

/// <summary>
/// Adds and removes pieces of a library folder (report.csv + clean_png\ svg\ thumbs\): the rows keep the columns of the
/// triage report; ids continue after the largest id of the report (every row, usable or not).
/// </summary>
public static class LibraryEditor
{
    public static readonly string[] Header = ["id", "file", "folder", "status", "text", "tier", "width", "height", "duplicate_of", "similar_to", "clean_png", "svg", "thumb", "note"];

    /// <summary>The text of a piece from its file name, like the triage: no numbers, (2), Latin letters, brackets or kashida.</summary>
    public static string CleanName(string stem)
    {
        var s = stem ?? "";
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\(\s*\d+\s*\)", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[\]\[~_\-.,،]+", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[0-9\u0660-\u0669]+", " ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"[A-Za-z]+", " ");
        s = s.Replace("ـ", "");
        return System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>A file-name-safe base: 00042_محمد.</summary>
    public static string BaseName(int id, string text)
    {
        var safe = string.Join("_", (text ?? "").Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        return safe.Length > 0 ? $"{id:00000}_{safe}" : $"{id:00000}";
    }

    public static List<string[]> ReadRows(string folder)
    {
        var f = Path.Combine(folder, "report.csv");
        return System.IO.File.Exists(f) ? Csv.Read(System.IO.File.ReadAllText(f, Encoding.UTF8)) : [];
    }

    /// <summary>The next free id (after every row of the report and every id already <paramref name="used"/>, e.g. kept segmentations).</summary>
    public static int NextId(string folder, IEnumerable<int>? used = null)
    {
        var rows = ReadRows(folder);
        int max = 0;
        if (rows.Count > 0)
        {
            int col = Array.FindIndex(rows[0], h => h.Trim().TrimStart('\uFEFF') == "id");
            if (col < 0) col = 0;
            foreach (var r in rows.Skip(1))
                if (col < r.Length && int.TryParse(r[col], out var v) && v > max && v < ArtboardStore.FirstId) max = v;
        }
        foreach (var u in used ?? []) if (u > max && u < ArtboardStore.FirstId) max = u;
        return max + 1;
    }

    /// <summary>Adds a row (by column name); the report is created with the triage header when missing.</summary>
    public static void AddRow(string folder, IReadOnlyDictionary<string, string> values)
    {
        Directory.CreateDirectory(folder);
        var rows = ReadRows(folder);
        if (rows.Count == 0) rows.Add([.. Header]);
        var head = rows[0].Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();
        rows.Add(head.Select(h => values.TryGetValue(h, out var v) ? v : "").ToArray());
        Write(folder, rows);
    }

    /// <summary>Removes the rows of these ids; returns how many were removed.</summary>
    public static int RemoveRows(string folder, IReadOnlyCollection<int> ids)
    {
        var rows = ReadRows(folder);
        if (rows.Count == 0) return 0;
        int col = Array.FindIndex(rows[0], h => h.Trim().TrimStart('\uFEFF') == "id");
        if (col < 0) return 0;
        int before = rows.Count;
        rows = rows.Take(1).Concat(rows.Skip(1).Where(r => !(col < r.Length && int.TryParse(r[col], out var v) && ids.Contains(v)))).ToList();
        if (rows.Count == before) return 0;
        Write(folder, rows);
        return before - rows.Count;
    }

    private static void Write(string folder, List<string[]> rows)
    {
        var sb = new StringBuilder();
        rows[0] = rows[0].Select(h => h.TrimStart('\uFEFF')).ToArray();
        foreach (var r in rows) sb.Append(string.Join(",", r.Select(Csv.Quote))).Append("\r\n");
        var f = Path.Combine(folder, "report.csv");
        var tmp = f + ".tmp";
        System.IO.File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(true));   // with BOM: Excel reads Arabic
        System.IO.File.Move(tmp, f, overwrite: true);
    }
}

/// <summary>Minimal RFC 4180 reader (quotes, commas and new lines inside quotes).</summary>
public static class Csv
{
    public static string Quote(string v)
    {
        v ??= "";
        return v.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    public static List<string[]> Read(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else cell.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(cell.ToString()); cell.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(cell.ToString()); cell.Clear();
                    if (row.Count > 1 || row[0].Length > 0) rows.Add([.. row]);
                    row.Clear();
                    break;
                default: cell.Append(c); break;
            }
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add([.. row]); }
        return rows;
    }
}
