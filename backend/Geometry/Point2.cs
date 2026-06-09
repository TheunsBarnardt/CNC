namespace Backend.Geometry;

/// <summary>A 2D point in table coordinates (mm, X right, Y up).</summary>
public readonly record struct Point2(double X, double Y)
{
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Point2 operator *(Point2 p, double s) => new(p.X * s, p.Y * s);

    public double DistanceTo(Point2 other)
    {
        double dx = X - other.X, dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
