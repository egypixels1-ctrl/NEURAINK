using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Neuraink.App.Services;

/// <summary>White artboard pages and vector PDF files.</summary>
public static class PaperWriter
{
    /// <summary>A white page (the picture of a new artboard).</summary>
    public static void WhitePng(string path, int width, int height)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    /// <summary>
    /// A one-page vector PDF: the shapes filled pure black on a white page of the piece's size (1 px = 0.75 pt, i.e. 96 dpi),
    /// as editable paths (curves kept as Bézier curves) — opens in Illustrator.
    /// </summary>
    public static void Pdf(string path, IEnumerable<Geometry> shapes, double width, double height)
    {
        const double k = 0.75;
        double H = height;
        var c = new StringBuilder();
        string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        string P(Point p) => $"{F(p.X * k)} {F((H - p.Y) * k)}";

        c.Append("1 1 1 rg 0 0 ").Append(F(width * k)).Append(' ').Append(F(height * k)).Append(" re f\n");
        c.Append("0 0 0 rg\n");
        foreach (var shape in shapes)
        {
            var pg = PathGeometry.CreateFromGeometry(shape);
            // curves the PDF does not have (arcs) are flattened finely; the rest stays as Bézier curves
            if (pg.Figures.SelectMany(f => f.Segments).Any(s => s is ArcSegment))
                pg = pg.GetFlattenedPathGeometry(0.02, ToleranceType.Absolute);
            foreach (var fig in pg.Figures)
            {
                var cur = fig.StartPoint;
                c.Append(P(cur)).Append(" m\n");
                foreach (var seg in fig.Segments)
                {
                    switch (seg)
                    {
                        case LineSegment l: c.Append(P(l.Point)).Append(" l\n"); cur = l.Point; break;
                        case PolyLineSegment pl: foreach (var q in pl.Points) c.Append(P(q)).Append(" l\n"); if (pl.Points.Count > 0) cur = pl.Points[^1]; break;
                        case BezierSegment b: c.Append(P(b.Point1)).Append(' ').Append(P(b.Point2)).Append(' ').Append(P(b.Point3)).Append(" c\n"); cur = b.Point3; break;
                        case PolyBezierSegment pb:
                            for (int i = 0; i + 2 < pb.Points.Count; i += 3)
                                c.Append(P(pb.Points[i])).Append(' ').Append(P(pb.Points[i + 1])).Append(' ').Append(P(pb.Points[i + 2])).Append(" c\n");
                            if (pb.Points.Count > 0) cur = pb.Points[^1];
                            break;
                        case QuadraticBezierSegment qb:
                            {
                                var c1 = cur + (qb.Point1 - cur) * (2.0 / 3); var c2 = qb.Point2 + (qb.Point1 - qb.Point2) * (2.0 / 3);
                                c.Append(P(c1)).Append(' ').Append(P(c2)).Append(' ').Append(P(qb.Point2)).Append(" c\n"); cur = qb.Point2;
                                break;
                            }
                        case PolyQuadraticBezierSegment pq:
                            for (int i = 0; i + 1 < pq.Points.Count; i += 2)
                            {
                                var ctl = pq.Points[i]; var end = pq.Points[i + 1];
                                var c1 = cur + (ctl - cur) * (2.0 / 3); var c2 = end + (ctl - end) * (2.0 / 3);
                                c.Append(P(c1)).Append(' ').Append(P(c2)).Append(' ').Append(P(end)).Append(" c\n"); cur = end;
                            }
                            break;
                    }
                }
                if (fig.IsClosed) c.Append("h\n");
            }
            c.Append(pg.FillRule == FillRule.EvenOdd ? "f*\n" : "f\n");
        }

        var content = Encoding.ASCII.GetBytes(c.ToString());
        var objs = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(width * k)} {F(height * k)}] /Contents 4 0 R /Resources << >> >>",
        };
        using var ms = new MemoryStream();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        var offsets = new List<long>();
        W("%PDF-1.4\n%âãÏÓ\n".Replace("âãÏÓ", "NEUR"));
        for (int i = 0; i < objs.Count; i++) { offsets.Add(ms.Position); W($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        offsets.Add(ms.Position);
        W($"4 0 obj\n<< /Length {content.Length} >>\nstream\n");
        ms.Write(content, 0, content.Length);
        W("\nendstream\nendobj\n");
        long xref = ms.Position;
        W($"xref\n0 5\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:0000000000} 00000 n \n");
        W($"trailer\n<< /Size 5 /Root 1 0 R /Info << /Producer (NEURAINK) >> >>\nstartxref\n{xref}\n%%EOF\n");
        File.WriteAllBytes(path, ms.ToArray());
    }
}
