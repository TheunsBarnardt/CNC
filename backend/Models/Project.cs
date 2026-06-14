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
public sealed class ImportedFile : Observable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    /// <summary>Original file name (e.g. "bracket.svg").</summary>
    public required string FileName { get; init; }
    /// <summary>
    /// When this file was split from a multi-layer import, the GroupId shared
    /// by all sibling sub-files (and their parts). Null for standalone imports.
    /// </summary>
    public Guid? GroupId { get; set; }
    private string _displayName = "";
    /// <summary>User-editable display name.</summary>
    public required string DisplayName
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }
    /// <summary>Alias for DisplayName — used by UI bindings.</summary>
    public string Name
    {
        get => _displayName;
        set => SetField(ref _displayName, value);
    }
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
    /// <summary>True if this is a bitmap file that can be traced.</summary>
    public bool IsBitmap => Kind == ImportedFileKind.Bitmap;
    private bool _visible = true;
    public bool Visible
    {
        get => _visible;
        set => SetField(ref _visible, value);
    }
    public List<PathGeometry> Paths { get; init; } = [];

    /// <summary>Warnings produced during import (unsupported entities etc.).</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>Per-layer color extracted from SVG/DXF (layer name → hex string like "#ff0000").</summary>
    public Dictionary<string, string> LayerColors { get; } = new();

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

    public bool HasWarnings => Warnings.Count > 0;
    public string WarningsSummary => Warnings.Count == 0 ? "" : string.Join("\n", Warnings);

    public string SummaryLine
    {
        get
        {
            var bb  = BoundingBox;
            var pts = Paths.Sum(p => p.Polyline.Points.Count);
            var parts = new List<string>();
            if (Paths.Count > 0)
                parts.Add($"{Paths.Count} path{(Paths.Count == 1 ? "" : "s")} · {pts} pts");
            if (bb.Width > 0 && bb.Height > 0)
                parts.Add($"{bb.Width:F0}×{bb.Height:F0} mm");
            return string.Join("  ", parts);
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
public sealed class Part : Observable
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required Guid FileId { get; init; }
    private double _x;
    public double X { get => _x; set => SetField(ref _x, value); }
    private double _y;
    public double Y { get => _y; set => SetField(ref _y, value); }
    private double _rotationDeg;
    public double RotationDeg { get => _rotationDeg; set => SetField(ref _rotationDeg, value); }
    private double _scaleX = 1.0;
    /// <summary>Horizontal scale factor (1 = natural, -1 = mirrored).</summary>
    public double ScaleX { get => _scaleX; set => SetField(ref _scaleX, value); }
    private double _scaleY = 1.0;
    /// <summary>Vertical scale factor (1 = natural, -1 = mirrored).</summary>
    public double ScaleY { get => _scaleY; set => SetField(ref _scaleY, value); }
    private Guid? _layerId;
    /// <summary>Layer this part belongs to. Null = default (first) layer.</summary>
    public Guid? LayerId { get => _layerId; set => SetField(ref _layerId, value); }
    private bool _isCutout;
    /// <summary>
    /// When true, CAM treats all paths of this part as inside cuts (holes),
    /// overriding the automatic containment-depth classification.
    /// </summary>
    public bool IsCutout { get => _isCutout; set => SetField(ref _isCutout, value); }

    private int _tabCount;
    /// <summary>Number of holding tabs to insert (0 = none). Plasma/router only.</summary>
    public int TabCount { get => _tabCount; set => SetField(ref _tabCount, value); }

    private double _tabWidthMm = 5.0;
    /// <summary>Width of each holding tab in mm (default 5 mm).</summary>
    public double TabWidthMm { get => _tabWidthMm; set => SetField(ref _tabWidthMm, value); }

    /// <summary>
    /// When non-null, this part belongs to a group of parts that move together.
    /// All parts with the same GroupId were created from the same multi-layer import
    /// and should maintain their relative positions when any one is dragged.
    /// </summary>
    public Guid? GroupId { get; set; }
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
public sealed class Layer : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string _name = "Layer 1";
    public string Name { get => _name; set => SetField(ref _name, value); }
    private string _color = "#3b82f6";
    /// <summary>CSS colour string used as the layer accent in the UI.</summary>
    public string Color { get => _color; set => SetField(ref _color, value); }
    private bool _visible = true;
    public bool Visible { get => _visible; set => SetField(ref _visible, value); }
    private bool _locked = false;
    public bool Locked { get => _locked; set => SetField(ref _locked, value); }
    private LayerOperationMode _operationMode = LayerOperationMode.Cut;
    /// <summary>Processing mode for paths on this layer. Defaults to Cut.</summary>
    public LayerOperationMode OperationMode { get => _operationMode; set => SetField(ref _operationMode, value); }
    /// <summary>Per-layer feed rate override (mm/min). Null = use global CAM setting.</summary>
    public double? FeedRateMmMinOverride { get; set; }
    /// <summary>Per-layer laser power override (0–100 %). Null = use global CAM setting.</summary>
    public double? LaserPowerPercentOverride { get; set; }
    /// <summary>True when the currently selected part belongs to this layer. Set by the VM — UI only.</summary>
    private bool _isActive;
    public bool IsActive { get => _isActive; set => SetField(ref _isActive, value); }
}

/// <summary>
/// A positioning guide line on the viewport canvas.
/// AngleDeg 90 = vertical line (defined by X), 0 = horizontal line (defined by Y).
/// For angled guides both X and Y define the pass-through point.
/// </summary>
public sealed class Guide
{
    public Guid   Id       { get; set; } = Guid.NewGuid();
    public string Label    { get; set; } = "";
    public double X        { get; set; }
    public double Y        { get; set; }
    /// <summary>Angle in degrees CCW from horizontal (0 = horizontal, 90 = vertical).</summary>
    public double AngleDeg { get; set; } = 90;
    public bool   IsLocked { get; set; }
}

/// <summary>
/// A rectangular exclusion zone on the table (machine clamp, fixture, hazard area).
/// The post-processor warns if any rapid or cut path enters the zone.
/// </summary>
public sealed class NoGoZone : Observable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    private string _label = "No-go zone";
    public string Label { get => _label; set => SetField(ref _label, value); }
    private double _x;
    public double X { get => _x; set => SetField(ref _x, value); }
    private double _y;
    public double Y { get => _y; set => SetField(ref _y, value); }
    private double _width = 50;
    public double Width { get => _width; set => SetField(ref _width, value); }
    private double _height = 50;
    public double Height { get => _height; set => SetField(ref _height, value); }
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
    public List<Guide> Guides { get; init; } = [];
    public List<NoGoZone> NoGoZones { get; init; } = [];

    // Convenience shortcuts used by desktop code
    public double TableWidthMm  => Table.WidthMm;
    public double TableHeightMm => Table.HeightMm;
}
