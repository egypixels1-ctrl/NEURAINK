using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Neuraink.Core;

namespace Neuraink.App.Services;

/// <summary>
/// A picture added to the library by the user: cleaned to pure black on pure white (whatever it was: grey edges,
/// transparency, light ink on dark), cropped with a margin like the triage, saved as clean_png\00042_text.png, traced to
/// svg\00042_text.svg, a thumbnail in thumbs\, and a row in report.csv.
/// </summary>
public static class PieceImporter
{
    public const int ThumbSide = 260;

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff"];
    public static bool IsPicture(string file) => Extensions.Contains(Path.GetExtension(file).ToLowerInvariant());

    /// <summary>A piece added: its id, the hash of its ink, and the files made (to take them back if the user cancels).</summary>
    public sealed record Added(int Id, string Hash, List<string> Files);

    /// <summary>The same picture is in the library already (the same ink, pixel for pixel).</summary>
    public sealed class DuplicateException(string message) : Exception(message);

    /// <summary>
    /// Adds one picture. Throws with a readable message when it cannot be used, and <see cref="DuplicateException"/> when the
    /// same ink is in the library already (<paramref name="knownHashes"/>; the new hash is added to it).
    /// </summary>
    public static Added Import(string file, string libraryFolder, IEnumerable<int> usedIds, ISet<string>? knownHashes = null)
    {
        var text = LibraryEditor.CleanName(Path.GetFileNameWithoutExtension(file));
        if (text.Length == 0) throw new InvalidOperationException(Loc.T("Lib.NoName"));
        var src = Decode(file) ?? throw new InvalidOperationException(Loc.T("Board.BadPicture"));

        var (ink, w, h) = Clean(src);
        if (w == 0) throw new InvalidOperationException(Loc.T("Lib.NoInk"));
        var hash = InkHash(ink, w, h);
        if (knownHashes != null && !knownHashes.Add(hash)) throw new DuplicateException(Loc.T("Lib.Duplicate"));

        Directory.CreateDirectory(libraryFolder);
        foreach (var d in new[] { "clean_png", "svg", "thumbs" }) Directory.CreateDirectory(Path.Combine(libraryFolder, d));
        int id = LibraryEditor.NextId(libraryFolder, usedIds);
        var name = LibraryEditor.BaseName(id, text);
        string png = $"clean_png/{name}.png", svg = $"svg/{name}.svg", thumb = $"thumbs/{name}.jpg";

        var clean = ToBitmap(ink, w, h);
        Save(new PngBitmapEncoder(), clean, Path.Combine(libraryFolder, "clean_png", name + ".png"));
        File.WriteAllText(Path.Combine(libraryFolder, "svg", name + ".svg"), Tracer.ToSvg(ink, w, h), new System.Text.UTF8Encoding(false));
        double s = Math.Min(1, ThumbSide / (double)Math.Max(w, h));
        var small = new TransformedBitmap(clean, new ScaleTransform(s, s));
        Save(new JpegBitmapEncoder { QualityLevel = 85 }, small, Path.Combine(libraryFolder, "thumbs", name + ".jpg"));

        LibraryEditor.AddRow(libraryFolder, new Dictionary<string, string>
        {
            ["id"] = id.ToString(), ["file"] = file, ["folder"] = "NEURAINK", ["status"] = "clean", ["text"] = text,
            ["width"] = w.ToString(), ["height"] = h.ToString(),
            ["clean_png"] = png, ["svg"] = svg, ["thumb"] = thumb,
            ["note"] = "added " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["hash"] = hash,
        });
        return new Added(id, hash, [Path.Combine(libraryFolder, "clean_png", name + ".png"), Path.Combine(libraryFolder, "svg", name + ".svg"),
            Path.Combine(libraryFolder, "thumbs", name + ".jpg")]);
    }

    /// <summary>The fingerprint of the ink (its size and every pixel): the same picture gives the same hash, whatever its name.</summary>
    public static string InkHash(bool[] ink, int w, int h)
    {
        var bytes = new byte[8 + (ink.Length + 7) / 8];
        BitConverter.GetBytes(w).CopyTo(bytes, 0);
        BitConverter.GetBytes(h).CopyTo(bytes, 4);
        for (int i = 0; i < ink.Length; i++) if (ink[i]) bytes[8 + i / 8] |= (byte)(1 << (i % 8));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))[..32];
    }

    private static BitmapSource? Decode(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            var dec = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = dec.Frames[0];
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            bgra.Freeze();
            return bgra;
        }
        catch { return null; }
    }

    /// <summary>
    /// Black and white: transparency over white, grey by luminance, Otsu's threshold; light ink on a dark ground is turned
    /// over; specks of a few pixels go; cropped to the ink with a margin (6 % of its size + 8 px, like the triage).
    /// </summary>
    public static (bool[] Ink, int W, int H) Clean(BitmapSource bgra)
    {
        int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        bgra.CopyPixels(px, stride, 0);
        var gray = new byte[w * h];
        var hist = new long[256];
        for (int i = 0; i < w * h; i++)
        {
            double a = px[i * 4 + 3] / 255.0;
            double l = 0.114 * px[i * 4] + 0.587 * px[i * 4 + 1] + 0.299 * px[i * 4 + 2];
            var g = (byte)Math.Round(l * a + 255 * (1 - a));
            gray[i] = g;
            hist[g]++;
        }
        int t = Otsu(hist, w * h);
        // the ground is the colour of the border
        long border = 0, bn = 0;
        for (int x = 0; x < w; x++) { border += gray[x] + gray[(h - 1) * w + x]; bn += 2; }
        for (int y = 0; y < h; y++) { border += gray[y * w] + gray[y * w + w - 1]; bn += 2; }
        bool lightInk = border / (double)bn < t;
        var ink = new bool[w * h];
        for (int i = 0; i < w * h; i++) ink[i] = lightInk ? gray[i] > t : gray[i] <= t;
        RemoveSpecks(ink, w, h, Math.Max(12, (int)(w * (long)h * 0.00004)));

        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (ink[y * w + x]) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
        if (x1 < 0) return ([], 0, 0);
        int pad = (int)(0.06 * Math.Max(x1 - x0, y1 - y0)) + 8;
        int cx0 = x0 - pad, cy0 = y0 - pad, cw = x1 - x0 + 1 + 2 * pad, ch = y1 - y0 + 1 + 2 * pad;
        var outp = new bool[cw * ch];
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                outp[(y - cy0) * cw + (x - cx0)] = ink[y * w + x];
        return (outp, cw, ch);
    }

    private static int Otsu(long[] hist, long total)
    {
        double sum = 0;
        for (int i = 0; i < 256; i++) sum += i * (double)hist[i];
        double sumB = 0, best = -1; long wB = 0; int t = 127;
        for (int i = 0; i < 256; i++)
        {
            wB += hist[i];
            if (wB == 0) continue;
            long wF = total - wB;
            if (wF == 0) break;
            sumB += i * (double)hist[i];
            double mB = sumB / wB, mF = (sum - sumB) / wF;
            double between = wB * (double)wF * (mB - mF) * (mB - mF);
            if (between > best) { best = between; t = i; }
        }
        return t;
    }

    /// <summary>Ink islands smaller than <paramref name="min"/> pixels (dust) are removed; dots of letters are far bigger.</summary>
    private static void RemoveSpecks(bool[] ink, int w, int h, int min)
    {
        var seen = new bool[w * h];
        var stack = new Stack<int>();
        var island = new List<int>();
        for (int s = 0; s < w * h; s++)
        {
            if (!ink[s] || seen[s]) continue;
            island.Clear();
            stack.Push(s); seen[s] = true;
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                island.Add(i);
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        int j = ny * w + nx;
                        if (ink[j] && !seen[j]) { seen[j] = true; stack.Push(j); }
                    }
            }
            if (island.Count < min) foreach (var i in island) ink[i] = false;
        }
    }

    private static BitmapSource ToBitmap(bool[] ink, int w, int h)
    {
        var px = new byte[w * h];
        for (int i = 0; i < px.Length; i++) px[i] = ink[i] ? (byte)0 : (byte)255;
        var b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, px, w);
        b.Freeze();
        return b;
    }

    private static void Save(BitmapEncoder enc, BitmapSource b, string path)
    {
        enc.Frames.Add(BitmapFrame.Create(b));
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp)) enc.Save(fs);
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>Files to the Windows Recycle Bin (they can be restored from there).</summary>
public static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOp
    {
        public IntPtr Hwnd;
        public uint Func;
        [MarshalAs(UnmanagedType.LPWStr)] public string From;
        [MarshalAs(UnmanagedType.LPWStr)] public string? To;
        public ushort Flags;
        public int AnyAborted;
        public IntPtr NameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Title;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOp op);

    private const uint FoDelete = 3;
    private const ushort Silent = 0x4, NoConfirmation = 0x10, AllowUndo = 0x40, NoErrorUi = 0x400;

    public static void Send(IReadOnlyCollection<string> files)
    {
        if (files.Count == 0) return;
        var op = new ShFileOp
        {
            Func = FoDelete,
            From = string.Join("\0", files.Select(Path.GetFullPath)) + "\0\0",
            Flags = (ushort)(Silent | NoConfirmation | AllowUndo | NoErrorUi),
        };
        int r = SHFileOperation(ref op);
        if (r != 0 && files.Any(File.Exists)) throw new IOException(Loc.F("Lib.RecycleFailed", r));
    }
}
