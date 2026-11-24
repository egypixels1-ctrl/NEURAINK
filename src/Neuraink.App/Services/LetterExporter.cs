using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Neuraink.Core;

namespace Neuraink.App.Services;

/// <summary>
/// Cuts every segmented letter out of the clean picture: the ink inside the letter's regions (body + dots)
/// becomes a transparent PNG in the letter library, with its data (form, joins, polygons) next to it.
/// </summary>
public static class LetterExporter
{
    public static int Export(LibraryItem item, Annotation ann, string cleanPng, LetterLibrary letters, string style)
    {
        letters.RemoveSource(item.Id);
        if (ann.State is not ("done" or "partial")) return 0;
        var src = Images.Load(cleanPng);
        if (src == null) return 0;

        int w = src.PixelWidth, h = src.PixelHeight;
        var gray = new FormatConvertedBitmap(src, PixelFormats.Gray8, null, 0);
        var ink = new byte[w * h];
        gray.CopyPixels(ink, w, 0);

        int count = 0;
        if (string.IsNullOrEmpty(ann.Split)) ann.Split = DataVersion.SplitFor(ann.Text);

        // ---- letters drawn with the brush: the cut-out is rendered from the strokes (a smooth vector) + an SVG
        var strokeChips = new HashSet<int>();
        foreach (var group in ann.Segments.Where(s => s.Kind == "stroke" && s.Stroke is { Count: > 0 } && s.Letter.Length > 0).GroupBy(s => s.Chip))
        {
            var first = group.First();
            var outline = StrokeGeometry.Outline(group);
            var path = letters.PathFor(first.Letter, item.Id, first.Chip, "png");
            if (!WriteVector(outline, path, out var box)) continue;
            strokeChips.Add(first.Chip);
            var body = ann.Segments.FirstOrDefault(s => s.Kind == "letter" && s.Chip == first.Chip);
            letters.WriteMeta(path, new LetterMeta
            {
                Letter = first.Letter, Form = first.Form, SourceId = item.Id, SourceText = ann.Text, SourceFile = item.File,
                Style = style, Tier = item.Tier, Chip = first.Chip, FromStrokes = true, SourceKind = "brush", Split = ann.Split,
                Strokes = group.Concat(ann.Segments.Where(d => d.Kind == "diacritic" && d.Chip == first.Chip)).OrderBy(d => d.Order).ToList(),
                BBox = [(int)Math.Floor(box.X), (int)Math.Floor(box.Y), (int)Math.Ceiling(box.Right), (int)Math.Ceiling(box.Bottom)],
                Entry = body?.Entry, Exit = body?.Exit, ImageSize = [w, h],
            });
            count++;
        }

        // ---- decorations drawn with the brush (tashkeel, ornaments): Letters\ornament
        int k = 0;
        foreach (var orn in ann.Segments.Where(s => s.Kind is "ornament" or "diacritic" && s.Stroke is { Count: > 0 }))
        {
            var dir = Path.Combine(letters.Folder, orn.Kind);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{item.Id:00000}_{k++:00}.png");
            if (!WriteVector(StrokeGeometry.Outline([orn]), path, out var box)) continue;
            letters.WriteMeta(path, new LetterMeta
            {
                Letter = orn.Letter, Form = orn.Kind == "diacritic" ? orn.Form : "ornament", SourceId = item.Id, SourceText = ann.Text, SourceFile = item.File, Style = style, Tier = item.Tier,
                Chip = orn.Kind == "diacritic" ? orn.Chip : -1, FromStrokes = true, Strokes = [orn], ImageSize = [w, h], SourceKind = "brush", Split = ann.Split,
                BBox = [(int)Math.Floor(box.X), (int)Math.Floor(box.Y), (int)Math.Ceiling(box.Right), (int)Math.Ceiling(box.Bottom)],
            });
        }

        foreach (var group in ann.Segments.Where(s => s.Kind is "letter" or "dots" && s.Points.Count >= 3 && s.Letter.Length > 0)
                                          .GroupBy(s => s.Chip))
        {
            var body = group.FirstOrDefault(s => s.Kind == "letter");
            if (body == null || strokeChips.Contains(body.Chip)) continue;
            var mask = RenderMask(group.Select(s => s.Points).ToList(), w, h);

            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            var outPx = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (mask[i * 4 + 3] < 128 || ink[i] >= 128) continue;   // outside the regions, or paper
                    outPx[i * 4 + 3] = 255;                                    // black ink, opaque (BGRA: 0,0,0,255)
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            if (x1 < 0) continue;

            var full = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, outPx, w * 4);
            var crop = new CroppedBitmap(full, new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
            var path = letters.PathFor(body.Letter, item.Id, body.Chip, "png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(crop));
            using (var fs = File.Create(path)) enc.Save(fs);

            letters.WriteMeta(path, new LetterMeta
            {
                Letter = body.Letter, Form = body.Form, SourceId = item.Id, SourceText = ann.Text, SourceFile = item.File,
                Style = style, Tier = item.Tier, Chip = body.Chip, SourceKind = "picture", Split = ann.Split, BBox = [x0, y0, x1 + 1, y1 + 1],
                Polygons = group.Select(s => s.Points).ToList(),
                Beziers = group.Where(s => s.Bezier is { Count: > 0 }).Select(s => s.Bezier!).ToList(), Entry = body.Entry, Exit = body.Exit, ImageSize = [w, h],
            });
            count++;
        }
        return count;
    }

    /// <summary>The outline as a black PNG (transparent background, picture pixels) and an SVG next to it.</summary>
    private static bool WriteVector(Geometry outline, string pngPath, out Rect box)
    {
        box = outline.Bounds;
        if (box.IsEmpty || box.Width < 1 || box.Height < 1) return false;
        int pw = (int)Math.Ceiling(box.Width) + 2, ph = (int)Math.Ceiling(box.Height) + 2;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform(-box.X + 1, -box.Y + 1));
            dc.DrawGeometry(Brushes.Black, null, outline);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using (var fs = File.Create(pngPath)) enc.Save(fs);
        File.WriteAllText(Path.ChangeExtension(pngPath, ".svg"), StrokeGeometry.ToSvg(outline, box));
        return true;
    }

    /// <summary>The regions filled white on a transparent bitmap of the picture's size (BGRA bytes).</summary>
    private static byte[] RenderMask(List<List<double[]>> polygons, int w, int h)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            foreach (var poly in polygons)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(poly[0][0], poly[0][1]), true, true);
                    g.PolyLineTo(poly.Skip(1).Select(p => new Point(p[0], p[1])).ToList(), true, false);
                }
                geo.Freeze();
                dc.DrawGeometry(Brushes.White, null, geo);
            }
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var px = new byte[w * h * 4];
        rtb.CopyPixels(px, w * 4, 0);
        return px;
    }
}
