using Backend.Geometry;
using Backend.Models;
using netDxf;
using netDxf.Entities;
using netDxf.Units;

namespace Backend.Import.Dxf;

/// <summary>
/// Imports DXF files via netDxf into the neutral geometry model.
///
/// Supported: lines, 2D polylines (incl. bulge arcs), arcs, circles, ellipses,
/// splines. 3D entities are flattened onto XY with a warning. Everything is
/// converted to mm using the drawing's $INSUNITS header (assuming mm when the
/// drawing is unitless, the common case for DIY CAD exports).
/// </summary>
public sealed class DxfImporter : IFileImporter
{
    public IReadOnlySet<string> Extensions { get; } = new HashSet<string> { ".dxf" };

    public ImportedFile Import(Stream content, string fileName)
    {
        // netDxf needs a seekable stream (it sniffs the file version first).
        using var buffer = new MemoryStream();
        content.CopyTo(buffer);
        buffer.Position = 0;

        DxfDocument doc;
        try
        {
            doc = DxfDocument.Load(buffer)
                ?? throw new FormatException("Unrecognized or pre-AutoCAD-2000 DXF file.");
        }
        catch (FormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FormatException($"Failed to parse DXF: {ex.Message}", ex);
        }

        var file = new ImportedFile
        {
            FileName = fileName,
            DisplayName = Path.GetFileNameWithoutExtension(fileName),
            Kind = ImportedFileKind.Dxf,
        };

        double scale = UnitScaleToMm(doc.DrawingVariables.InsUnits, file.Warnings);
        double tol = CurveFlattener.DefaultToleranceMm / scale;

        foreach (var line in doc.Entities.Lines)
        {
            AddPath(file, line.Layer.Name, scale,
                [new(line.StartPoint.X, line.StartPoint.Y), new(line.EndPoint.X, line.EndPoint.Y)],
                closed: false);
        }

        foreach (var pl in doc.Entities.Polylines2D)
        {
            var pts = FlattenPolyline2D(pl, tol);
            AddPath(file, pl.Layer.Name, scale, pts, pl.IsClosed);
        }

        foreach (var pl in doc.Entities.Polylines3D)
        {
            file.Warnings.Add($"3D polyline on layer '{pl.Layer.Name}' flattened onto XY.");
            var pts = pl.Vertexes.Select(v => new Point2(v.X, v.Y)).ToList();
            AddPath(file, pl.Layer.Name, scale, pts, pl.IsClosed);
        }

        foreach (var arc in doc.Entities.Arcs)
        {
            var pts = SampleArc(
                new Point2(arc.Center.X, arc.Center.Y), arc.Radius,
                arc.StartAngle, arc.EndAngle, tol);
            AddPath(file, arc.Layer.Name, scale, pts, closed: false);
        }

        foreach (var circle in doc.Entities.Circles)
        {
            var pts = SampleArc(
                new Point2(circle.Center.X, circle.Center.Y), circle.Radius,
                0, 360, tol);
            // Full circle: drop duplicated end point and mark closed.
            pts.RemoveAt(pts.Count - 1);
            AddPath(file, circle.Layer.Name, scale, pts, closed: true);
        }

        foreach (var ellipse in doc.Entities.Ellipses)
        {
            var pts = SampleEllipse(ellipse, tol, out bool closed);
            AddPath(file, ellipse.Layer.Name, scale, pts, closed);
        }

        foreach (var spline in doc.Entities.Splines)
        {
            // netDxf polygonalizes NURBS for us; precision = sample count.
            var sampled = spline.PolygonalVertexes(SplineSamples(spline));
            var pts = sampled.Select(v => new Point2(v.X, v.Y)).ToList();
            AddPath(file, spline.Layer.Name, scale, pts, spline.IsClosed);
        }

        int texts = doc.Entities.Texts.Count() + doc.Entities.MTexts.Count();
        if (texts > 0)
            file.Warnings.Add($"{texts} text entit{(texts == 1 ? "y" : "ies")} skipped — explode text to outlines in CAD.");
        int inserts = doc.Entities.Inserts.Count();
        if (inserts > 0)
            file.Warnings.Add($"{inserts} block insert(s) skipped — explode blocks before exporting.");

        if (file.Paths.Count == 0 && file.Warnings.Count == 0)
            file.Warnings.Add("No supported vector geometry found in the DXF.");

        GeometryNormalizer.NormalizeToOrigin(file);
        return file;
    }

    private static void AddPath(
        ImportedFile file, string layer, double scale, List<Point2> pts, bool closed)
    {
        if (pts.Count < 2) return;
        var scaled = pts.Select(p => new Point2(p.X * scale, p.Y * scale)).ToList();
        file.Paths.Add(new PathGeometry
        {
            Layer = layer,
            Polyline = new Polyline2 { Points = scaled, IsClosed = closed },
        });
    }

    /// <summary>2D polyline → points, expanding bulge segments into arcs.</summary>
    private static List<Point2> FlattenPolyline2D(Polyline2D pl, double tol)
    {
        var pts = new List<Point2>();
        var vertexes = pl.Vertexes;
        int count = vertexes.Count;
        for (int i = 0; i < count; i++)
        {
            var v = vertexes[i];
            var p0 = new Point2(v.Position.X, v.Position.Y);
            if (pts.Count == 0 || pts[^1].DistanceTo(p0) > 1e-12) pts.Add(p0);

            bool isLast = i == count - 1;
            if (isLast && !pl.IsClosed) break;
            if (Math.Abs(v.Bulge) < 1e-12) continue;

            var p1 = isLast
                ? new Point2(vertexes[0].Position.X, vertexes[0].Position.Y)
                : new Point2(vertexes[i + 1].Position.X, vertexes[i + 1].Position.Y);

            // Bulge = tan(included angle / 4); sign = CCW positive.
            double theta = 4 * Math.Atan(v.Bulge);
            double chord = p0.DistanceTo(p1);
            if (chord < 1e-12) continue;
            double radius = chord / (2 * Math.Sin(Math.Abs(theta) / 2));

            // Arc center: perpendicular offset from chord midpoint.
            double mx = (p0.X + p1.X) / 2, my = (p0.Y + p1.Y) / 2;
            double h = Math.Sqrt(Math.Max(0, radius * radius - chord * chord / 4));
            double ux = (p1.X - p0.X) / chord, uy = (p1.Y - p0.Y) / chord;
            // Perpendicular pointing left of travel; flips with bulge sign and arc size.
            double side = Math.Sign(v.Bulge) * (Math.Abs(theta) > Math.PI ? -1 : 1);
            double cx = mx - side * h * uy;
            double cy = my + side * h * ux;

            double a0 = Math.Atan2(p0.Y - cy, p0.X - cx);
            int segments = ArcSegments(radius, Math.Abs(theta), tol);
            for (int s = 1; s < segments; s++)
            {
                double a = a0 + theta * s / segments;
                pts.Add(new Point2(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a)));
            }
            if (!isLast) { /* next vertex point added on next iteration */ }
            else if (pl.IsClosed) { /* closes back to vertex 0 — no extra point */ }
        }
        return pts;
    }

    /// <summary>Samples a CCW arc (angles in degrees, DXF convention) including both endpoints.</summary>
    private static List<Point2> SampleArc(
        Point2 center, double radius, double startDeg, double endDeg, double tol)
    {
        double sweep = endDeg - startDeg;
        if (sweep <= 0) sweep += 360;
        double a0 = startDeg * Math.PI / 180;
        double sweepRad = sweep * Math.PI / 180;
        int segments = ArcSegments(radius, sweepRad, tol);

        var pts = new List<Point2>(segments + 1);
        for (int i = 0; i <= segments; i++)
        {
            double a = a0 + sweepRad * i / segments;
            pts.Add(new Point2(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)));
        }
        return pts;
    }

    private static List<Point2> SampleEllipse(Ellipse e, double tol, out bool closed)
    {
        closed = e.IsFullEllipse;
        double major = e.MajorAxis / 2, minor = e.MinorAxis / 2;
        double rot = e.Rotation * Math.PI / 180;
        double cos = Math.Cos(rot), sin = Math.Sin(rot);

        double start = e.StartAngle * Math.PI / 180;
        double end = e.EndAngle * Math.PI / 180;
        double sweep = closed ? 2 * Math.PI : end - start;
        if (sweep <= 0) sweep += 2 * Math.PI;

        int segments = ArcSegments(Math.Max(major, minor), sweep, tol);
        int last = closed ? segments - 1 : segments; // closed: skip duplicate end point
        var pts = new List<Point2>(last + 1);
        for (int i = 0; i <= last; i++)
        {
            double a = start + sweep * i / segments;
            double x = major * Math.Cos(a), y = minor * Math.Sin(a);
            pts.Add(new Point2(
                e.Center.X + x * cos - y * sin,
                e.Center.Y + x * sin + y * cos));
        }
        return pts;
    }

    /// <summary>Segment count so the chord (sagitta) error stays under tolerance.</summary>
    internal static int ArcSegments(double radius, double sweepRad, double tol)
    {
        if (radius <= tol) return 4;
        double maxStep = 2 * Math.Acos(Math.Max(-1, 1 - tol / radius));
        int n = (int)Math.Ceiling(sweepRad / maxStep);
        return Math.Clamp(n, 4, 2048);
    }

    private static int SplineSamples(Spline spline)
    {
        int controls = spline.ControlPoints.Count();
        return Math.Clamp(controls * 16, 64, 1024);
    }

    private static double UnitScaleToMm(DrawingUnits units, List<string> warnings)
    {
        switch (units)
        {
            case DrawingUnits.Millimeters: return 1;
            case DrawingUnits.Centimeters: return 10;
            case DrawingUnits.Meters: return 1000;
            case DrawingUnits.Inches: return 25.4;
            case DrawingUnits.Feet: return 304.8;
            case DrawingUnits.Unitless:
                warnings.Add("DXF has no units ($INSUNITS=0) — assuming millimeters.");
                return 1;
            default:
                warnings.Add($"Unhandled DXF units '{units}' — assuming millimeters.");
                return 1;
        }
    }
}
