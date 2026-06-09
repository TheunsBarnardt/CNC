using Backend.Geometry;

namespace Backend.Import.Svg;

/// <summary>
/// Converts SVG elliptical-arc segments to cubic béziers (the W3C
/// endpoint-to-center parameterization, then one cubic per ≤90° slice).
/// Approximation error for a 90° slice is far below our flattening tolerance.
/// </summary>
internal static class SvgArc
{
    public static void ToCubics(
        List<SvgSegment> sink,
        Point2 from,
        double rx,
        double ry,
        double xAxisRotationDeg,
        bool largeArc,
        bool sweep,
        Point2 to)
    {
        // Spec: zero radii → straight line; negative radii → absolute value.
        rx = Math.Abs(rx);
        ry = Math.Abs(ry);
        if (rx < 1e-12 || ry < 1e-12 || from == to)
        {
            sink.Add(new SvgSegment.Line(to));
            return;
        }

        double phi = xAxisRotationDeg * Math.PI / 180.0;
        double cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);

        // Step 1: midpoint coordinates (x1', y1').
        double dx2 = (from.X - to.X) / 2.0;
        double dy2 = (from.Y - to.Y) / 2.0;
        double x1p = cosPhi * dx2 + sinPhi * dy2;
        double y1p = -sinPhi * dx2 + cosPhi * dy2;

        // Spec: scale radii up if they cannot span the endpoints.
        double lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lambda > 1)
        {
            double s = Math.Sqrt(lambda);
            rx *= s;
            ry *= s;
        }

        // Step 2: center (cx', cy').
        double rx2 = rx * rx, ry2 = ry * ry;
        double num = rx2 * ry2 - rx2 * y1p * y1p - ry2 * x1p * x1p;
        double den = rx2 * y1p * y1p + ry2 * x1p * x1p;
        double coef = Math.Sqrt(Math.Max(0, num / den));
        if (largeArc == sweep) coef = -coef;
        double cxp = coef * (rx * y1p / ry);
        double cyp = coef * (-ry * x1p / rx);

        // Step 3: center in original coordinates.
        double cx = cosPhi * cxp - sinPhi * cyp + (from.X + to.X) / 2.0;
        double cy = sinPhi * cxp + cosPhi * cyp + (from.Y + to.Y) / 2.0;

        // Step 4: start angle and sweep extent.
        double theta1 = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double dTheta = Angle(
            (x1p - cxp) / rx, (y1p - cyp) / ry,
            (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && dTheta > 0) dTheta -= 2 * Math.PI;
        else if (sweep && dTheta < 0) dTheta += 2 * Math.PI;

        // Slice into ≤90° pieces, one cubic each.
        int slices = Math.Max(1, (int)Math.Ceiling(Math.Abs(dTheta) / (Math.PI / 2)));
        double delta = dTheta / slices;
        // Magic-number control-point distance for a circular slice of angle delta.
        double t = 4.0 / 3.0 * Math.Tan(delta / 4.0);

        double theta = theta1;
        for (int i = 0; i < slices; i++)
        {
            double thetaNext = theta + delta;
            var p0 = PointAt(theta);
            var p3 = PointAt(thetaNext);
            var d0 = DerivativeAt(theta);
            var d3 = DerivativeAt(thetaNext);
            var c1 = new Point2(p0.X + t * d0.X, p0.Y + t * d0.Y);
            var c2 = new Point2(p3.X - t * d3.X, p3.Y - t * d3.Y);
            // Force the final endpoint to land exactly on the requested point.
            var end = i == slices - 1 ? to : p3;
            sink.Add(new SvgSegment.Cubic(c1, c2, end));
            theta = thetaNext;
        }

        Point2 PointAt(double angle)
        {
            double ct = Math.Cos(angle), st = Math.Sin(angle);
            return new Point2(
                cx + rx * ct * cosPhi - ry * st * sinPhi,
                cy + rx * ct * sinPhi + ry * st * cosPhi);
        }

        Point2 DerivativeAt(double angle)
        {
            double ct = Math.Cos(angle), st = Math.Sin(angle);
            return new Point2(
                -rx * st * cosPhi - ry * ct * sinPhi,
                -rx * st * sinPhi + ry * ct * cosPhi);
        }

        static double Angle(double ux, double uy, double vx, double vy)
        {
            double dot = ux * vx + uy * vy;
            double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
            double ang = Math.Acos(Math.Clamp(dot / len, -1, 1));
            return (ux * vy - uy * vx) < 0 ? -ang : ang;
        }
    }
}
