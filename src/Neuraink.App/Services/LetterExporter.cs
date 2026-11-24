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
        foreach (var group in ann.Segments.Where(s => s.Kind is "letter" or "dots" && s.Points.Count >= 3 && s.Letter.Length > 0)
                                          .GroupBy(s => s.Chip))
        {
            var body = group.FirstOrDefault(s => s.Kind == "letter");
            if (body == null) continue;
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
                Style = style, Tier = item.Tier, Chip = body.Chip, BBox = [x0, y0, x1 + 1, y1 + 1],
                Polygons = group.Select(s => s.Points).ToList(),
                Beziers = group.Where(s => s.Bezier is { Count: > 0 }).Select(s => s.Bezier!).ToList(), Entry = body.Entry, Exit = body.Exit, ImageSize = [w, h],
            });
            count++;
        }
        return count;
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
