using Backend.Geometry;
using Backend.Models;

namespace Backend.Import;

/// <summary>
/// Generates synthetic shapes (rectangles, circles, polygons, etc.) as ImportedFile objects
/// suitable for adding directly to a project.
/// </summary>
public static class ShapeGenerator
{
    /// <summary>Generate a rectangle shape.</summary>
    public static ImportedFile CreateRectangle(double widthMm, double heightMm, double cornerRadiusMm = 0)
    {
        var points = new List<Point2>();
        int segments = cornerRadiusMm > 0 ? 8 : 4;

        if (cornerRadiusMm <= 0)
        {
            // Sharp corners
            points.Add(new Point2(0, 0));
            points.Add(new Point2(widthMm, 0));
            points.Add(new Point2(widthMm, heightMm));
            points.Add(new Point2(0, heightMm));
        }
        else
        {
            // Rounded corners (approximate with line segments)
            double r = Math.Min(cornerRadiusMm, Math.Min(widthMm, heightMm) / 2);
            double arcSteps = 4;

            // Bottom-left corner
            for (int i = 0; i < arcSteps; i++)
            {
                double angle = (i / arcSteps) * Math.PI / 2;
                points.Add(new Point2(r - r * Math.Cos(angle), r - r * Math.Sin(angle)));
            }

            // Bottom-right corner
            for (int i = 0; i < arcSteps; i++)
            {
                double angle = (i / arcSteps) * Math.PI / 2;
                points.Add(new Point2(widthMm - r + r * Math.Sin(angle), r - r * Math.Cos(angle)));
            }

            // Top-right corner
            for (int i = 0; i < arcSteps; i++)
            {
                double angle = (i / arcSteps) * Math.PI / 2;
                points.Add(new Point2(widthMm - r + r * Math.Cos(angle), heightMm - r + r * Math.Sin(angle)));
            }

            // Top-left corner
            for (int i = 0; i < arcSteps; i++)
            {
                double angle = (i / arcSteps) * Math.PI / 2;
                points.Add(new Point2(r - r * Math.Sin(angle), heightMm - r + r * Math.Cos(angle)));
            }
        }

        var polyline = new Polyline2 { Points = points, IsClosed = true };
        var path = new PathGeometry { Polyline = polyline };

        return new ImportedFile
        {
            FileName = "rectangle.shp",
            DisplayName = "Rectangle",
            Kind = ImportedFileKind.Shape,
            Paths = [path]
        };
    }

    /// <summary>Generate a circle shape.</summary>
    public static ImportedFile CreateCircle(double radiusMm)
    {
        var points = new List<Point2>();
        int segments = Math.Max(12, (int)(radiusMm / 5) * 4); // More segments for larger circles

        for (int i = 0; i < segments; i++)
        {
            double angle = (i / (double)segments) * Math.PI * 2;
            double x = radiusMm + radiusMm * Math.Cos(angle);
            double y = radiusMm + radiusMm * Math.Sin(angle);
            points.Add(new Point2(x, y));
        }

        var polyline = new Polyline2 { Points = points, IsClosed = true };
        var path = new PathGeometry { Polyline = polyline };

        return new ImportedFile
        {
            FileName = "circle.shp",
            DisplayName = "Circle",
            Kind = ImportedFileKind.Shape,
            Paths = [path]
        };
    }

    /// <summary>Generate an ellipse shape.</summary>
    public static ImportedFile CreateEllipse(double widthMm, double heightMm)
    {
        var points = new List<Point2>();
        int segments = Math.Max(16, (int)(Math.Max(widthMm, heightMm) / 5) * 4);

        double rx = widthMm / 2;
        double ry = heightMm / 2;

        for (int i = 0; i < segments; i++)
        {
            double angle = (i / (double)segments) * Math.PI * 2;
            double x = rx + rx * Math.Cos(angle);
            double y = ry + ry * Math.Sin(angle);
            points.Add(new Point2(x, y));
        }

        var polyline = new Polyline2 { Points = points, IsClosed = true };
        var path = new PathGeometry { Polyline = polyline };

        return new ImportedFile
        {
            FileName = "ellipse.shp",
            DisplayName = "Ellipse",
            Kind = ImportedFileKind.Shape,
            Paths = [path]
        };
    }

    /// <summary>Generate a regular polygon (n-sided).</summary>
    public static ImportedFile CreatePolygon(int sideCount, double radiusMm)
    {
        if (sideCount < 3) sideCount = 3;

        var points = new List<Point2>();
        double centerX = radiusMm;
        double centerY = radiusMm;

        for (int i = 0; i < sideCount; i++)
        {
            double angle = (i / (double)sideCount) * Math.PI * 2 - Math.PI / 2;
            double x = centerX + radiusMm * Math.Cos(angle);
            double y = centerY + radiusMm * Math.Sin(angle);
            points.Add(new Point2(x, y));
        }

        var polyline = new Polyline2 { Points = points, IsClosed = true };
        var path = new PathGeometry { Polyline = polyline };

        return new ImportedFile
        {
            FileName = "polygon.shp",
            DisplayName = $"{sideCount}-gon",
            Kind = ImportedFileKind.Shape,
            Paths = [path]
        };
    }

    /// <summary>Generate a star shape.</summary>
    public static ImportedFile CreateStar(int pointCount, double outerRadiusMm, double innerRadiusMm)
    {
        if (pointCount < 3) pointCount = 3;

        var points = new List<Point2>();
        double centerX = outerRadiusMm;
        double centerY = outerRadiusMm;

        for (int i = 0; i < pointCount * 2; i++)
        {
            double angle = (i / (double)(pointCount * 2)) * Math.PI * 2 - Math.PI / 2;
            double radius = i % 2 == 0 ? outerRadiusMm : innerRadiusMm;
            double x = centerX + radius * Math.Cos(angle);
            double y = centerY + radius * Math.Sin(angle);
            points.Add(new Point2(x, y));
        }

        var polyline = new Polyline2 { Points = points, IsClosed = true };
        var path = new PathGeometry { Polyline = polyline };

        return new ImportedFile
        {
            FileName = "star.shp",
            DisplayName = $"{pointCount}-point star",
            Kind = ImportedFileKind.Shape,
            Paths = [path]
        };
    }

    /// <summary>Generate a line shape (open polyline).</summary>
    public static ImportedFile CreateLine(double lengthMm)
    {
        var points = new List<Point2>
        {
            new Point2(0, 0),
            new Point2(lengthMm, 0)
        };

        var polyline = new Polyline2 { Points = points, IsClosed = false };
        var path = new PathGeometry { Polyline = polyline };

        return new ImportedFile
        {
            FileName = "line.shp",
            DisplayName = "Line",
            Kind = ImportedFileKind.Shape,
            Paths = [path]
        };
    }
}
