using System.Globalization;
using Backend.Geometry;

namespace Backend.Import.Svg;

/// <summary>
/// SVG 2x3 affine transform [a c e / b d f]. Used to accumulate the current
/// transform matrix (CTM) while walking the element tree, so geometry comes
/// out already in document coordinates.
/// </summary>
public readonly record struct SvgMatrix(double A, double B, double C, double D, double E, double F)
{
    public static SvgMatrix Identity => new(1, 0, 0, 1, 0, 0);

    public static SvgMatrix Translation(double tx, double ty) => new(1, 0, 0, 1, tx, ty);

    public static SvgMatrix Scaling(double sx, double sy) => new(sx, 0, 0, sy, 0, 0);

    public static SvgMatrix Rotation(double degrees)
    {
        double r = degrees * Math.PI / 180.0;
        double cos = Math.Cos(r), sin = Math.Sin(r);
        return new SvgMatrix(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>this ∘ other — applies <paramref name="other"/> first, then this.</summary>
    public SvgMatrix Multiply(SvgMatrix o) => new(
        A * o.A + C * o.B,
        B * o.A + D * o.B,
        A * o.C + C * o.D,
        B * o.C + D * o.D,
        A * o.E + C * o.F + E,
        B * o.E + D * o.F + F);

    public Point2 Apply(Point2 p) => new(A * p.X + C * p.Y + E, B * p.X + D * p.Y + F);

    /// <summary>Rough overall scale factor — used to convert a mm flattening tolerance into local units.</summary>
    public double ApproximateScale()
    {
        // Geometric mean of the two basis-vector lengths; exact for uniform scale.
        double sx = Math.Sqrt(A * A + B * B);
        double sy = Math.Sqrt(C * C + D * D);
        double s = Math.Sqrt(sx * sy);
        return s > 1e-12 ? s : 1.0;
    }

    /// <summary>
    /// Parses an SVG transform attribute, e.g.
    /// "translate(10 20) rotate(45) matrix(1,0,0,1,5,5)".
    /// </summary>
    public static SvgMatrix Parse(string? transform)
    {
        var result = Identity;
        if (string.IsNullOrWhiteSpace(transform)) return result;

        int i = 0;
        while (i < transform.Length)
        {
            // Skip separators to the next function name.
            while (i < transform.Length && !char.IsLetter(transform[i])) i++;
            if (i >= transform.Length) break;

            int nameStart = i;
            while (i < transform.Length && char.IsLetter(transform[i])) i++;
            string name = transform[nameStart..i];

            int open = transform.IndexOf('(', i);
            if (open < 0) break;
            int close = transform.IndexOf(')', open);
            if (close < 0) break;
            var args = ParseNumbers(transform[(open + 1)..close]);
            i = close + 1;

            SvgMatrix m = name switch
            {
                "translate" => Translation(Arg(args, 0), Arg(args, 1)),
                "scale" => Scaling(Arg(args, 0, 1), args.Count > 1 ? args[1] : Arg(args, 0, 1)),
                "rotate" when args.Count >= 3 =>
                    Translation(args[1], args[2])
                        .Multiply(Rotation(args[0]))
                        .Multiply(Translation(-args[1], -args[2])),
                "rotate" => Rotation(Arg(args, 0)),
                "skewX" => new SvgMatrix(1, 0, Math.Tan(Arg(args, 0) * Math.PI / 180), 1, 0, 0),
                "skewY" => new SvgMatrix(1, Math.Tan(Arg(args, 0) * Math.PI / 180), 0, 1, 0, 0),
                "matrix" when args.Count >= 6 =>
                    new SvgMatrix(args[0], args[1], args[2], args[3], args[4], args[5]),
                _ => Identity,
            };
            result = result.Multiply(m);
        }
        return result;

        static double Arg(List<double> args, int index, double fallback = 0) =>
            index < args.Count ? args[index] : fallback;
    }

    internal static List<double> ParseNumbers(string s)
    {
        var values = new List<double>();
        foreach (var token in s.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                values.Add(v);
        }
        return values;
    }
}
