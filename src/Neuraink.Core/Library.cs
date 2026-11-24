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

/// <summary>Minimal RFC 4180 reader (quotes, commas and new lines inside quotes).</summary>
public static class Csv
{
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
