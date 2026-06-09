using System.Globalization;
using Backend.Geometry;

namespace Backend.Import.Svg;

/// <summary>One segment of a parsed subpath. Everything curved is normalized to cubics.</summary>
public abstract record SvgSegment
{
    public sealed record Line(Point2 To) : SvgSegment;
    public sealed record Cubic(Point2 C1, Point2 C2, Point2 To) : SvgSegment;
}

public sealed class SvgSubPath
{
    public Point2 Start { get; init; }
    public List<SvgSegment> Segments { get; } = [];
    public bool Closed { get; set; }
}

/// <summary>
/// Parses an SVG path "d" attribute into subpaths of line/cubic segments.
///
/// Quadratics are promoted to cubics and arcs are converted to cubic
/// approximations (≤90° per cubic), so downstream only handles two segment
/// kinds. Output is in SVG user units, untransformed — the importer applies
/// the CTM to the (control) points afterwards, which is exact for affine
/// transforms of béziers.
/// </summary>
public static class SvgPathParser
{
    public static List<SvgSubPath> Parse(string d)
    {
        var scanner = new Scanner(d);
        var result = new List<SvgSubPath>();
        SvgSubPath? sub = null;

        Point2 current = default;
        Point2 subStart = default;
        // Previous control points for S/T smooth shortcuts.
        Point2? prevCubicC2 = null;
        Point2? prevQuadC = null;
        char prevCmd = '\0';

        while (scanner.TryReadCommand(prevCmd, out char cmd))
        {
            bool rel = char.IsLower(cmd);
            char op = char.ToUpperInvariant(cmd);

            switch (op)
            {
                case 'M':
                {
                    var p = scanner.ReadPoint();
                    current = rel ? current + p : p;
                    subStart = current;
                    sub = new SvgSubPath { Start = current };
                    result.Add(sub);
                    // Subsequent coordinate pairs after M are implicit linetos.
                    prevCmd = rel ? 'l' : 'L';
                    prevCubicC2 = prevQuadC = null;
                    continue;
                }
                case 'L':
                {
                    var p = scanner.ReadPoint();
                    current = rel ? current + p : p;
                    Sub().Segments.Add(new SvgSegment.Line(current));
                    break;
                }
                case 'H':
                {
                    double x = scanner.ReadNumber();
                    current = new Point2(rel ? current.X + x : x, current.Y);
                    Sub().Segments.Add(new SvgSegment.Line(current));
                    break;
                }
                case 'V':
                {
                    double y = scanner.ReadNumber();
                    current = new Point2(current.X, rel ? current.Y + y : y);
                    Sub().Segments.Add(new SvgSegment.Line(current));
                    break;
                }
                case 'C':
                {
                    var c1 = Abs(scanner.ReadPoint());
                    var c2 = Abs(scanner.ReadPoint());
                    var to = Abs(scanner.ReadPoint());
                    Sub().Segments.Add(new SvgSegment.Cubic(c1, c2, to));
                    prevCubicC2 = c2;
                    current = to;
                    prevCmd = cmd;
                    prevQuadC = null;
                    continue;
                }
                case 'S':
                {
                    // First control point reflects the previous cubic's C2 (or
                    // equals the current point when the previous segment wasn't a cubic).
                    var c1 = prevCubicC2 is { } pc ? current + (current - pc) : current;
                    var c2 = Abs(scanner.ReadPoint());
                    var to = Abs(scanner.ReadPoint());
                    Sub().Segments.Add(new SvgSegment.Cubic(c1, c2, to));
                    prevCubicC2 = c2;
                    current = to;
                    prevCmd = cmd;
                    prevQuadC = null;
                    continue;
                }
                case 'Q':
                {
                    var qc = Abs(scanner.ReadPoint());
                    var to = Abs(scanner.ReadPoint());
                    AddQuad(Sub(), current, qc, to);
                    prevQuadC = qc;
                    current = to;
                    prevCmd = cmd;
                    prevCubicC2 = null;
                    continue;
                }
                case 'T':
                {
                    var qc = prevQuadC is { } pq ? current + (current - pq) : current;
                    var to = Abs(scanner.ReadPoint());
                    AddQuad(Sub(), current, qc, to);
                    prevQuadC = qc;
                    current = to;
                    prevCmd = cmd;
                    prevCubicC2 = null;
                    continue;
                }
                case 'A':
                {
                    double rx = scanner.ReadNumber();
                    double ry = scanner.ReadNumber();
                    double rot = scanner.ReadNumber();
                    bool largeArc = scanner.ReadFlag();
                    bool sweep = scanner.ReadFlag();
                    var to = Abs(scanner.ReadPoint());
                    SvgArc.ToCubics(Sub().Segments, current, rx, ry, rot, largeArc, sweep, to);
                    current = to;
                    prevCmd = cmd;
                    prevCubicC2 = prevQuadC = null;
                    continue;
                }
                case 'Z':
                {
                    if (sub is not null)
                    {
                        sub.Closed = true;
                        current = subStart;
                    }
                    prevCmd = cmd;
                    prevCubicC2 = prevQuadC = null;
                    continue;
                }
                default:
                    throw new FormatException($"Unsupported SVG path command '{cmd}'.");
            }

            prevCmd = cmd;
            prevCubicC2 = prevQuadC = null;

            Point2 Abs(Point2 p) => rel ? current + p : p;
        }

        return result;

        SvgSubPath Sub() =>
            sub ?? throw new FormatException("SVG path data must start with a moveto (M/m).");
    }

    private static void AddQuad(SvgSubPath sub, Point2 from, Point2 qc, Point2 to)
    {
        // Exact quadratic→cubic promotion.
        var c1 = from + (qc - from) * (2.0 / 3.0);
        var c2 = to + (qc - to) * (2.0 / 3.0);
        sub.Segments.Add(new SvgSegment.Cubic(c1, c2, to));
    }

    /// <summary>
    /// Scanner over path data implementing SVG's number micro-syntax, where
    /// "1-2" is two numbers, "1.5.5" is 1.5 and .5, and arc flags may be
    /// packed without separators ("011" = flag 0, flag 1, number 1).
    /// </summary>
    private sealed class Scanner(string s)
    {
        private int _i;

        private void SkipSeparators()
        {
            while (_i < s.Length && (char.IsWhiteSpace(s[_i]) || s[_i] == ',')) _i++;
        }

        public bool TryReadCommand(char prevCmd, out char cmd)
        {
            SkipSeparators();
            if (_i >= s.Length) { cmd = '\0'; return false; }

            char c = s[_i];
            if (char.IsLetter(c))
            {
                _i++;
                cmd = c;
                return true;
            }

            // A number where a command could be = implicit repeat of the previous command.
            if (prevCmd != '\0' && prevCmd is not ('Z' or 'z'))
            {
                cmd = prevCmd;
                return true;
            }
            throw new FormatException($"Unexpected character '{c}' in SVG path data at {_i}.");
        }

        public double ReadNumber()
        {
            SkipSeparators();
            int start = _i;
            if (_i < s.Length && (s[_i] == '+' || s[_i] == '-')) _i++;
            while (_i < s.Length && char.IsAsciiDigit(s[_i])) _i++;
            if (_i < s.Length && s[_i] == '.')
            {
                _i++;
                while (_i < s.Length && char.IsAsciiDigit(s[_i])) _i++;
            }
            if (_i < s.Length && (s[_i] == 'e' || s[_i] == 'E'))
            {
                _i++;
                if (_i < s.Length && (s[_i] == '+' || s[_i] == '-')) _i++;
                while (_i < s.Length && char.IsAsciiDigit(s[_i])) _i++;
            }
            if (_i == start)
                throw new FormatException($"Expected number in SVG path data at {_i}.");
            return double.Parse(s[start.._i], CultureInfo.InvariantCulture);
        }

        public bool ReadFlag()
        {
            SkipSeparators();
            if (_i >= s.Length || (s[_i] != '0' && s[_i] != '1'))
                throw new FormatException($"Expected arc flag (0/1) in SVG path data at {_i}.");
            return s[_i++] == '1';
        }

        public Point2 ReadPoint() => new(ReadNumber(), ReadNumber());
    }
}
