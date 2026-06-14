using Backend.Geometry;
using Backend.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace Backend.Import;

/// <summary>
/// Imports a PDF by extracting vector paths from each page.
/// Simple PDFs (laser-cut templates, DXF-exported PDFs) yield exact geometry.
/// Raster-only PDFs produce a warning (vector paths will be empty).
/// </summary>
public static class PdfImporter
{
    private const double PtToMm = 25.4 / 72.0; // PDF points → mm

    public static ImportedFile Import(Stream stream, string fileName)
    {
        var paths    = new List<PathGeometry>();
        var warnings = new List<string>();

        using var doc = PdfDocument.Open(stream);
        int totalPages = doc.NumberOfPages;

        for (int p = 1; p <= totalPages; p++)
        {
            var page   = doc.GetPage(p);
            double pageH = page.Height * PtToMm;
            string? layerName = totalPages > 1 ? $"Page {p}" : null;

            // PdfPath is List<PdfSubpath>; page.Paths is IReadOnlyList<PdfPath>
            foreach (var pdfPath in page.Paths)
            {
                // Each PdfSubpath is an independent sub-contour.
                foreach (PdfSubpath subpath in pdfPath)
                {
                    bool closed = subpath.IsClosed();
                    var pts = new List<Point2>();
                    Point2 current = default;

                    // IPathCommand nested types are private; use dynamic for access.
                    foreach (var cmd in subpath.Commands)
                    {
                        string typeName = cmd.GetType().Name;
                        dynamic dc = cmd;
                        switch (typeName)
                        {
                            case "Move":
                            {
                                if (pts.Count > 1)
                                {
                                    paths.Add(MakePath(pts, closed, layerName));
                                    pts = [];
                                }
                                PdfPoint loc = dc.Location;
                                current = new Point2(loc.X * PtToMm, pageH - loc.Y * PtToMm);
                                pts.Add(current);
                                break;
                            }
                            case "Line":
                            {
                                PdfPoint to = dc.To;
                                current = new Point2(to.X * PtToMm, pageH - to.Y * PtToMm);
                                pts.Add(current);
                                break;
                            }
                            case "CubicBezierCurve":
                            {
                                PdfPoint sp = dc.StartPoint;
                                PdfPoint c1 = dc.FirstControlPoint;
                                PdfPoint c2 = dc.SecondControlPoint;
                                PdfPoint ep = dc.EndPoint;
                                FlattenCubic(pts,
                                    new Point2(sp.X * PtToMm, pageH - sp.Y * PtToMm),
                                    new Point2(c1.X * PtToMm, pageH - c1.Y * PtToMm),
                                    new Point2(c2.X * PtToMm, pageH - c2.Y * PtToMm),
                                    new Point2(ep.X * PtToMm, pageH - ep.Y * PtToMm), 0.1);
                                current = new Point2(ep.X * PtToMm, pageH - ep.Y * PtToMm);
                                break;
                            }
                            case "QuadraticBezierCurve":
                            {
                                PdfPoint sp = dc.StartPoint;
                                PdfPoint cp = dc.ControlPoint;
                                PdfPoint ep = dc.EndPoint;
                                // Elevate quadratic to cubic
                                var p0 = new Point2(sp.X * PtToMm, pageH - sp.Y * PtToMm);
                                var q  = new Point2(cp.X * PtToMm, pageH - cp.Y * PtToMm);
                                var p3 = new Point2(ep.X * PtToMm, pageH - ep.Y * PtToMm);
                                var c1 = new Point2(p0.X + 2.0/3 * (q.X - p0.X), p0.Y + 2.0/3 * (q.Y - p0.Y));
                                var c2 = new Point2(p3.X + 2.0/3 * (q.X - p3.X), p3.Y + 2.0/3 * (q.Y - p3.Y));
                                FlattenCubic(pts, p0, c1, c2, p3, 0.1);
                                current = p3;
                                break;
                            }
                            case "BezierCurve":
                            {
                                // Degenerate: just add end point
                                PdfPoint ep = dc.EndPoint;
                                current = new Point2(ep.X * PtToMm, pageH - ep.Y * PtToMm);
                                pts.Add(current);
                                break;
                            }
                            case "Close":
                                closed = true;
                                break;
                        }
                    }

                    if (pts.Count > 1)
                        paths.Add(MakePath(pts, closed, layerName));
                }
            }
        }

        if (paths.Count == 0)
            warnings.Add("No vector paths found in PDF. The file may be raster-only.");

        NormalizePaths(paths);

        var file = new ImportedFile
        {
            FileName    = fileName,
            DisplayName = Path.GetFileNameWithoutExtension(fileName),
            Kind        = ImportedFileKind.Svg, // same neutral model as SVG
        };
        file.Paths.AddRange(paths);
        file.Warnings.AddRange(warnings);
        return file;
    }

    private static PathGeometry MakePath(List<Point2> pts, bool closed, string? layer) =>
        new()
        {
            Layer    = layer,
            Polyline = new Polyline2 { IsClosed = closed, Points = [.. pts] },
        };

    private static void FlattenCubic(
        List<Point2> result, Point2 p0, Point2 c1, Point2 c2, Point2 p3, double tol)
    {
        double d = Math.Abs((c1.X - p0.X) * (p3.Y - p0.Y) - (c1.Y - p0.Y) * (p3.X - p0.X))
                 + Math.Abs((c2.X - p3.X) * (p3.Y - p0.Y) - (c2.Y - p3.Y) * (p3.X - p0.X));
        if (d <= tol * (p0.DistanceTo(p3) + 1))
        {
            result.Add(p3);
            return;
        }
        var m01  = Mid(p0, c1);  var m12  = Mid(c1, c2);
        var m23  = Mid(c2, p3);  var m012 = Mid(m01, m12);
        var m123 = Mid(m12, m23); var m   = Mid(m012, m123);
        FlattenCubic(result, p0, m01,  m012, m,  tol);
        FlattenCubic(result, m,  m123, m23,  p3, tol);
    }

    private static Point2 Mid(Point2 a, Point2 b) =>
        new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static void NormalizePaths(List<PathGeometry> paths)
    {
        if (paths.Count == 0) return;
        double minX = double.MaxValue, minY = double.MaxValue;
        foreach (var pg in paths)
        foreach (var pt in pg.Polyline.Points)
        {
            if (pt.X < minX) minX = pt.X;
            if (pt.Y < minY) minY = pt.Y;
        }
        if (minX == 0 && minY == 0) return;
        foreach (var pg in paths)
        {
            var pts = pg.Polyline.Points;
            for (int i = 0; i < pts.Count; i++)
                pts[i] = new Point2(pts[i].X - minX, pts[i].Y - minY);
        }
    }
}
