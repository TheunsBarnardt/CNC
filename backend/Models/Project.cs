using Backend.Cam;
using Backend.Geometry;

namespace Backend.Models;

/// <summary>Display/entry units. Geometry is ALWAYS stored in mm internally.</summary>
public enum Units
{
    Millimeters,
    Inches,
}

/// <summary>Where the machine's work origin (X0 Y0) sits on the table.</summary>
public enum TableOrigin
{
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
    Center,
}

/// <summary>Table/sheet setup for a project. All lengths in mm.</summary>
public sealed class TableSettings
{
    public double WidthMm { get; set; } = 1220;
    public double HeightMm { get; set; } = 1220;
    public TableOrigin Origin { get; set; } = TableOrigin.BottomLeft;
    public double MaterialThicknessMm { get; set; } = 2;
}

/// <summary>
/// One path from an imported file, with provenance (layer) kept so users can
/// later filter/assign operations per layer.
///
/// <para>Polyline.Points holds the anchor nodes. Handles, when present, store
/// per-node Bézier control points so the user can create smooth curves in the
/// node editor. Format per entry: [inX, inY, outX, outY] in local-file
/// millimetres, or null for a sharp corner. CAM flattens the curves to a
/// straight-segment polyline at toolpath-generation time.</para>
/// </summary>
public sealed class PathGeometry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Layer { get; init; }
    public required Polyline2 Polyline { get; init; }
    /// <summary>
    /// Optional per-node Bézier handles (length == Polyline.Points.Count).
    /// Each entry is either null (sharp corner) or double[4] = [inX,inY,outX,outY].
    /// </summary>
    public List<double[]?>? Handles { get; set; }
}

public enum ImportedFileKind
{
    Svg,
    Dxf,
    Shape,   // drawn/created in-app (not imported from file)
    Bitmap,  // raster image traced to vector
}

/// <summary>
/// An imported vector file parsed into neutral geometry. Geometry is stored in
/// the file's own local coordinate space (mm, Y-up, normalized so the bounds'
/// lower-left sits at 0,0); placement on the table is a separate transform
/// added in Task 2 (kept apart so auto-nesting can drive it later).
/// </summary>
public sealed class ImportedFile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Original file name (e.g. "bracket.svg").</summary>
    public required string FileName { get; init; }
    /// <summary>User-editable display name.</summary>
    public required string DisplayName { get; set; }
    /// <summary>Alias for DisplayName — used by UI bindings.</summary>
    public string Name { get => DisplayName; set => DisplayName = value; }
    public ImportedFileKind Kind { get; init; }
    /// <summary>Short string label for UI badge.</summary>
    public string KindLabel => Kind switch
    {
        ImportedFileKind.Svg    => "SVG",
        ImportedFileKind.Dxf    => "DXF",
        ImportedFileKind.Bitmap => "IMG",
        ImportedFileKind.Shape  => "SHP",
        _                       => "?",
    };
    public bool Visible { get; set; } = true;
    public List<PathGeometry> Paths { get; init; } = [];

    /// <summary>Warnings produced during import (unsupported entities etc.).</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>Axis-aligned bounding box of all paths in local file coordinates (mm).</summary>
    public (double X, double Y, double Width, double Height) BoundingBox
    {
        get
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var pg in Paths)
            foreach (var pt in pg.Polyline.Points)
            {
                if (pt.X < minX) minX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y > maxY) maxY = pt.Y;
            }
            return minX > maxX ? (0, 0, 0, 0) : (minX, minY, maxX - minX, maxY - minY);
        }
    }

    // ---- Bitmap-only fields (null for vector files) ----
    /// <summary>Original raster bytes, served by GET /api/project/files/{id}/bitmap-image.</summary>
    public byte[]? BitmapData { get; set; }
    /// <summary>MIME type of BitmapData ("image/png" or "image/jpeg").</summary>
    public string? BitmapMimeType { get; set; }
    /// <summary>Settings used for the most recent trace, stored as JSON for re-trace.</summary>
    public string? BitmapTraceSettingsJson { get; set; }
}

/// <summary>
/// A placed instance of an imported file on the table.
///
/// This is THE part-transform model the plan requires to be nesting-ready:
/// placement is exactly (translation X/Y in mm) + (rotation in degrees CCW
/// about the file's local bounding-box center). Manual dragging sets these
/// fields today; the auto-nester (Milestone 2) will set the same fields.
/// Duplicating a part = a second Part referencing the same file geometry.
/// </summary>
public sealed class Part
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FileId { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double RotationDeg { get; set; }
    /// <summary>Horizontal scale factor (1 = natural, -1 = mirrored).</summary>
    public double ScaleX { get; set; } = 1.0;
    /// <summary>Vertical scale factor (1 = natural, -1 = mirrored).</summary>
    public double ScaleY { get; set; } = 1.0;
    /// <summary>Layer this part belongs to. Null = default (first) layer.</summary>
    public Guid? LayerId { get; set; }
    /// <summary>
    /// When true, CAM treats all paths of this part as inside cuts (holes),
    /// overriding the automatic containment-depth classification.
    /// </summary>
    public bool IsCutout { get; set; }
}

/// <summary>
/// Processing operation assigned to a layer. Determines how paths on this
/// layer are treated by the CAM engine and post-processor.
/// </summary>
public enum LayerOperationMode
{
    /// <summary>Normal full-depth cut (default).</summary>
    Cut,
    /// <summary>Lighter pass — for laser: lower power; for plasma: same kerf at reduced feed.</summary>
    Score,
    /// <summary>Surface mark / engrave — for laser: low power fast pass; no pierce for plasma.</summary>
    Engrave,
}

/// <summary>A named layer that groups parts for visibility / lock control.</summary>
public sealed class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Layer 1";
    /// <summary>CSS colour string used as the layer accent in the UI.</summary>
    public string Color { get; set; } = "#3b82f6";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; } = false;
    /// <summary>Processing mode for paths on this layer. Defaults to Cut.</summary>
    public LayerOperationMode OperationMode { get; set; } = LayerOperationMode.Cut;
    /// <summary>Per-layer feed rate override (mm/min). Null = use global CAM setting.</summary>
    public double? FeedRateMmMinOverride { get; set; }
    /// <summary>Per-layer laser power override (0–100 %). Null = use global CAM setting.</summary>
    public double? LaserPowerPercentOverride { get; set; }
}

/// <summary>A whole project: table setup + imported files. Saved as one JSON file.</summary>
public sealed class Project
{
    /// <summary>Bumped when the persisted shape changes, for forward migration.</summary>
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Untitled";
    public Units Units { get; set; } = Units.Millimeters;
    public TableSettings Table { get; set; } = new();
    public CamSettings Cam { get; set; } = new();
    public List<ImportedFile> Files { get; init; } = [];
    public List<Part> Parts { get; init; } = [];
    public List<Layer> Layers { get; init; } = [new Layer()];

    // Convenience shortcuts used by desktop code
    public double TableWidthMm  => Table.WidthMm;
    public double TableHeightMm => Table.HeightMm;
}
