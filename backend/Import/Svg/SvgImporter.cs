using System.Globalization;
using System.Xml.Linq;
using Backend.Geometry;
using Backend.Models;

namespace Backend.Import.Svg;

/// <summary>
/// Imports SVG files into the neutral geometry model.
///
/// Supported: path, rect (incl. rounded), circle, ellipse, line, polyline,
/// polygon, nested g transforms. Unsupported elements (text, image, use, …)
/// are skipped with a warning — convert text to paths in the drawing app.
///
/// Coordinate handling: SVG is Y-down in "user units"; the table is Y-up in
/// mm. The importer converts user units to mm via the width/height + viewBox
/// attributes (falling back to the CSS 96 dpi px), flips Y, and normalizes the
/// file so its bounding box sits at (0,0).
/// </summary>
public sealed class SvgImporter : IFileImporter
{
    public IReadOnlySet<string> Extensions { get; } = new HashSet<string> { ".svg" };

    private static readonly XNamespace Ns       = "http://www.w3.org/2000/svg";
    private static readonly XNamespace Inkscape = "http://www.inkscape.org/namespaces/inkscape";

    public ImportedFile Import(Stream content, string fileName)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(content);
        }
        catch (Exception ex)
        {
            throw new FormatException($"Not a valid SVG/XML file: {ex.Message}", ex);
        }

        var root = doc.Root;
        if (root is null || root.Name.LocalName != "svg")
            throw new FormatException("File has no <svg> root element.");

        var file = new ImportedFile
        {
            FileName = fileName,
            DisplayName = Path.GetFileNameWithoutExtension(fileName),
            Kind = ImportedFileKind.Svg,
        };

        // Estimate the user-unit → mm scale up front (exact when width+viewBox
        // are declared) so curve flattening uses a tolerance that is ~mm-accurate.
        double preScale = PreliminaryScale(root);
        double tolUser = CurveFlattener.DefaultToleranceMm / preScale;

        // Collect raw subpaths (user units, Y-down, CTM applied).
        var collected = new List<(string? Layer, List<Point2> Points, bool Closed)>();
        Walk(root, SvgMatrix.Identity, layer: null, tolUser, collected, file.Warnings, file.LayerColors);

        if (collected.Count == 0)
        {
            file.Warnings.Add("No drawable vector geometry found.");
            return file;
        }

        // Establish user-unit → mm mapping and the Y-flip height.
        var (vbX, vbY, vbW, vbH) = ViewBox(root, collected);
        double widthMm = LengthMm(root.Attribute("width")?.Value) ?? vbW * PxToMm;
        double heightMm = LengthMm(root.Attribute("height")?.Value) ?? vbH * PxToMm;
        double sx = vbW > 1e-12 ? widthMm / vbW : PxToMm;
        double sy = vbH > 1e-12 ? heightMm / vbH : PxToMm;

        foreach (var (layer, points, closed) in collected)
        {
            var mapped = points
                .Select(p => new Point2((p.X - vbX) * sx, heightMm - (p.Y - vbY) * sy))
                .ToList();
            file.Paths.Add(new PathGeometry
            {
                Layer = layer,
                Polyline = new Polyline2 { Points = mapped, IsClosed = closed },
            });
        }

        GeometryNormalizer.NormalizeToOrigin(file);
        return file;
    }

    /// <summary>CSS reference pixel: 96 per inch.</summary>
    private const double PxToMm = 25.4 / 96.0;

    /// <summary>Best-effort user-unit → mm scale before geometry is parsed.</summary>
    private static double PreliminaryScale(XElement root)
    {
        var vb = SvgMatrix.ParseNumbers(root.Attribute("viewBox")?.Value ?? "");
        double? widthMm = LengthMm(root.Attribute("width")?.Value);
        if (vb.Count == 4 && vb[2] > 1e-12 && widthMm is { } w) return w / vb[2];
        return PxToMm;
    }

    private static void Walk(
        XElement element,
        SvgMatrix ctm,
        string? layer,
        double tolUser,
        List<(string?, List<Point2>, bool)> sink,
        List<string> warnings,
        Dictionary<string, string> layerColors)
    {
        var local = ctm.Multiply(SvgMatrix.Parse(element.Attribute("transform")?.Value));
        string name = element.Name.LocalName;

        switch (name)
        {
            case "svg" or "a":
                foreach (var child in element.Elements()) Walk(child, local, layer, tolUser, sink, warnings, layerColors);
                return;
            case "g":
            {
                // Only Inkscape layer groups (inkscape:groupmode="layer") define a new
                // layer boundary. Plain <g> groups are just transforms/grouping and
                // keep the current layer name so sub-paths stay on the right layer.
                bool isLayer = element.Attribute(Inkscape + "groupmode")?.Value == "layer";
                string? groupLayer = isLayer
                    ? (element.Attributes().FirstOrDefault(a => a.Name.LocalName == "label")?.Value
                       ?? element.Attribute("id")?.Value
                       ?? layer)
                    : layer;

                // Extract the dominant fill/stroke color from this Inkscape layer.
                if (isLayer && groupLayer is not null && !layerColors.ContainsKey(groupLayer))
                {
                    var col = ExtractGroupColor(element);
                    if (col is not null) layerColors[groupLayer] = col;
                }

                foreach (var child in element.Elements()) Walk(child, local, groupLayer, tolUser, sink, warnings, layerColors);
                return;
            }
            // Non-rendered containers and metadata.
            case "defs" or "symbol" or "clipPath" or "mask" or "marker" or "pattern"
                or "metadata" or "title" or "desc" or "style" or "filter"
                or "linearGradient" or "radialGradient" or "namedview" or "sodipodi":
                return;
            case "text" or "tspan":
                warnings.Add("<text> is not supported — convert text to paths before importing.");
                return;
            case "image":
                warnings.Add("<image> (raster) elements are skipped.");
                return;
            case "use":
                warnings.Add("<use> references are not supported — flatten/ungroup before importing.");
                return;
        }

        var subPaths = ShapeToSubPaths(element, warnings);
        if (subPaths is null) return;

        // Tighten the tolerance by the CTM's scale so transformed geometry
        // still flattens to ~mm accuracy.
        double scale = local.ApproximateScale();
        double tolLocal = tolUser / (scale > 1e-9 ? scale : 1);

        foreach (var sub in subPaths)
        {
            var points = new List<Point2> { local.Apply(sub.Start) };
            var current = sub.Start;
            foreach (var seg in sub.Segments)
            {
                switch (seg)
                {
                    case SvgSegment.Line line:
                        points.Add(local.Apply(line.To));
                        current = line.To;
                        break;
                    case SvgSegment.Cubic cubic:
                        CurveFlattener.FlattenCubic(
                            points,
                            local.Apply(current),
                            local.Apply(cubic.C1),
                            local.Apply(cubic.C2),
                            local.Apply(cubic.To),
                            tolLocal);
                        current = cubic.To;
                        break;
                }
            }

            // Drop duplicated closing point if the path already returns to start.
            if (sub.Closed && points.Count > 2 && points[0].DistanceTo(points[^1]) < 1e-9)
                points.RemoveAt(points.Count - 1);

            if (points.Count >= 2)
                sink.Add((layer, points, sub.Closed));
        }
    }

    private static List<SvgSubPath>? ShapeToSubPaths(XElement e, List<string> warnings)
    {
        switch (e.Name.LocalName)
        {
            case "path":
            {
                string? d = e.Attribute("d")?.Value;
                if (string.IsNullOrWhiteSpace(d)) return null;
                return SvgPathParser.Parse(d);
            }
            case "rect":
            {
                double x = Num(e, "x"), y = Num(e, "y");
                double w = Num(e, "width"), h = Num(e, "height");
                if (w <= 0 || h <= 0) return null;
                double rx = Num(e, "rx", double.NaN), ry = Num(e, "ry", double.NaN);
                if (double.IsNaN(rx)) rx = double.IsNaN(ry) ? 0 : ry;
                if (double.IsNaN(ry)) ry = rx;
                rx = Math.Min(Math.Abs(rx), w / 2);
                ry = Math.Min(Math.Abs(ry), h / 2);
                return rx > 0 && ry > 0
                    ? [RoundedRect(x, y, w, h, rx, ry)]
                    : [PolySubPath([new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)], closed: true)];
            }
            case "circle":
            {
                double cx = Num(e, "cx"), cy = Num(e, "cy"), r = Num(e, "r");
                return r > 0 ? [Ellipse(cx, cy, r, r)] : null;
            }
            case "ellipse":
            {
                double cx = Num(e, "cx"), cy = Num(e, "cy");
                double rx = Num(e, "rx"), ry = Num(e, "ry");
                return rx > 0 && ry > 0 ? [Ellipse(cx, cy, rx, ry)] : null;
            }
            case "line":
            {
                var sub = PolySubPath(
                    [new(Num(e, "x1"), Num(e, "y1")), new(Num(e, "x2"), Num(e, "y2"))],
                    closed: false);
                return [sub];
            }
            case "polyline" or "polygon":
            {
                var nums = SvgMatrix.ParseNumbers(e.Attribute("points")?.Value ?? "");
                if (nums.Count < 4) return null;
                var pts = new List<Point2>();
                for (int i = 0; i + 1 < nums.Count; i += 2) pts.Add(new Point2(nums[i], nums[i + 1]));
                return [PolySubPath(pts, closed: e.Name.LocalName == "polygon")];
            }
            default:
                warnings.Add($"<{e.Name.LocalName}> elements are not supported and were skipped.");
                return null;
        }
    }

    private static SvgSubPath PolySubPath(List<Point2> pts, bool closed)
    {
        var sub = new SvgSubPath { Start = pts[0], Closed = closed };
        foreach (var p in pts.Skip(1)) sub.Segments.Add(new SvgSegment.Line(p));
        return sub;
    }

    private static SvgSubPath Ellipse(double cx, double cy, double rx, double ry)
    {
        // Two 180° arcs starting at the rightmost point.
        var start = new Point2(cx + rx, cy);
        var sub = new SvgSubPath { Start = start, Closed = true };
        SvgArc.ToCubics(sub.Segments, start, rx, ry, 0, false, true, new Point2(cx - rx, cy));
        SvgArc.ToCubics(sub.Segments, new Point2(cx - rx, cy), rx, ry, 0, false, true, start);
        return sub;
    }

    private static SvgSubPath RoundedRect(double x, double y, double w, double h, double rx, double ry)
    {
        var sub = new SvgSubPath { Start = new Point2(x + rx, y), Closed = true };
        var segs = sub.Segments;
        segs.Add(new SvgSegment.Line(new Point2(x + w - rx, y)));
        SvgArc.ToCubics(segs, new Point2(x + w - rx, y), rx, ry, 0, false, true, new Point2(x + w, y + ry));
        segs.Add(new SvgSegment.Line(new Point2(x + w, y + h - ry)));
        SvgArc.ToCubics(segs, new Point2(x + w, y + h - ry), rx, ry, 0, false, true, new Point2(x + w - rx, y + h));
        segs.Add(new SvgSegment.Line(new Point2(x + rx, y + h)));
        SvgArc.ToCubics(segs, new Point2(x + rx, y + h), rx, ry, 0, false, true, new Point2(x, y + h - ry));
        segs.Add(new SvgSegment.Line(new Point2(x, y + ry)));
        SvgArc.ToCubics(segs, new Point2(x, y + ry), rx, ry, 0, false, true, new Point2(x + rx, y));
        return sub;
    }

    private static double Num(XElement e, string attr, double fallback = 0)
    {
        string? v = e.Attribute(attr)?.Value;
        return v is not null
            && double.TryParse(StripUnit(v), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d
            : fallback;
    }

    private static string StripUnit(string v)
    {
        v = v.Trim();
        int end = v.Length;
        while (end > 0 && !char.IsAsciiDigit(v[end - 1]) && v[end - 1] != '.') end--;
        return v[..end];
    }

    /// <summary>Parses an SVG length attribute (e.g. "100mm", "4in", "250") into mm.</summary>
    private static double? LengthMm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.EndsWith('%')) return null; // percentage of nothing meaningful here

        (string unit, double toMm)[] units =
        [
            ("mm", 1), ("cm", 10), ("in", 25.4), ("pt", 25.4 / 72), ("pc", 25.4 / 6), ("px", PxToMm),
        ];
        double factor = PxToMm; // unitless = px
        string num = value;
        foreach (var (unit, toMm) in units)
        {
            if (value.EndsWith(unit, StringComparison.OrdinalIgnoreCase))
            {
                factor = toMm;
                num = value[..^unit.Length];
                break;
            }
        }
        return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
            ? d * factor
            : null;
    }

    private static (double X, double Y, double W, double H) ViewBox(
        XElement root, List<(string?, List<Point2> Points, bool)> collected)
    {
        var vb = SvgMatrix.ParseNumbers(root.Attribute("viewBox")?.Value ?? "");
        if (vb.Count == 4 && vb[2] > 0 && vb[3] > 0) return (vb[0], vb[1], vb[2], vb[3]);

        // No usable viewBox — fall back to the geometry's own bounds so the
        // Y-flip still mirrors around the content.
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (_, points, _) in collected)
        {
            foreach (var p in points)
            {
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
        }
        return (minX, minY, Math.Max(1e-9, maxX - minX), Math.Max(1e-9, maxY - minY));
    }

    // ── Color extraction helpers ──────────────────────────────────────────

    /// <summary>Returns the first non-neutral hex fill or stroke color found in
    /// the group's descendant shapes. Used to colour project layers from SVG layers.</summary>
    private static string? ExtractGroupColor(XElement group)
    {
        foreach (var el in group.Descendants())
        {
            string eName = el.Name.LocalName;
            if (eName is not ("path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon"))
                continue;
            var color = ParseElementColor(el, "fill") ?? ParseElementColor(el, "stroke");
            if (color is not null) return color;
        }
        return null;
    }

    private static string? ParseElementColor(XElement el, string prop)
    {
        // Inline style takes priority over attribute.
        string? value = GetStyleProp(el.Attribute("style")?.Value, prop)
                     ?? el.Attribute(prop)?.Value?.Trim();
        if (string.IsNullOrEmpty(value) || value == "none") return null;
        // Only accept hex colours; skip "black", "white", rgb(), url() etc.
        if (!value.StartsWith('#')) return null;
        // Expand 3-digit shorthand → 6-digit.
        if (value.Length == 4)
            value = $"#{value[1]}{value[1]}{value[2]}{value[2]}{value[3]}{value[3]}";
        if (value.Length != 7) return null;
        // Skip near-black and near-white.
        if (value.Equals("#000000", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.Equals("#ffffff", StringComparison.OrdinalIgnoreCase)) return null;
        return value.ToLowerInvariant();
    }

    private static string? GetStyleProp(string? style, string prop)
    {
        if (style is null) return null;
        foreach (var part in style.Split(';'))
        {
            var idx = part.IndexOf(':');
            if (idx < 0) continue;
            if (part[..idx].Trim().Equals(prop, StringComparison.OrdinalIgnoreCase))
                return part[(idx + 1)..].Trim();
        }
        return null;
    }
}
