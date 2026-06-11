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
/// One flattened path from an imported file, with provenance (layer) kept so
/// users can later filter/assign operations per layer.
/// </summary>
public sealed class PathGeometry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Layer { get; init; }
    public required Polyline2 Polyline { get; init; }
}

public enum ImportedFileKind
{
    Svg,
    Dxf,
    Shape,  // drawn/created in-app (not imported from file)
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
    public ImportedFileKind Kind { get; init; }
    public bool Visible { get; set; } = true;
    public List<PathGeometry> Paths { get; init; } = [];

    /// <summary>Warnings produced during import (unsupported entities etc.).</summary>
    public List<string> Warnings { get; init; } = [];
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
}
