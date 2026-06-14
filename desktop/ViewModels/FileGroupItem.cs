using Backend.Models;

namespace Desktop.ViewModels;

/// <summary>
/// Represents one row in the Files panel: either a standalone imported file
/// or a group of sub-files created from a single multi-layer SVG/DXF import.
/// The group appears as one entry; actions (visibility, delete) apply to all members.
/// </summary>
public sealed class FileGroupItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string name) =>
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));

    public IReadOnlyList<ImportedFile> Members { get; }
    public bool IsGroup => Members.Count > 1;

    public string Name
    {
        get => IsGroup
            ? System.IO.Path.GetFileNameWithoutExtension(Members[0].FileName)
            : Members[0].DisplayName;
        set
        {
            if (!IsGroup) { Members[0].DisplayName = value; Notify(nameof(Name)); }
        }
    }

    /// <summary>"SVG" / "DXF" for singles; "N layers" for groups.</summary>
    public string KindLabel =>
        IsGroup ? $"{Members.Count} layers" : Members[0].KindLabel;

    public bool Visible
    {
        get => Members.Any(f => f.Visible);
        set { foreach (var f in Members) f.Visible = value; Notify(nameof(Visible)); }
    }

    public bool HasWarnings => Members.Any(f => f.HasWarnings);
    public string WarningsSummary => string.Join("\n", Members.SelectMany(f => f.Warnings));
    public bool IsBitmap => !IsGroup && Members[0].IsBitmap;

    public string SummaryLine
    {
        get
        {
            if (!IsGroup) return Members[0].SummaryLine;
            // Combined stats across all layers
            int paths = Members.Sum(f => f.Paths.Count);
            int pts   = Members.Sum(f => f.Paths.Sum(p => p.Polyline.Points.Count));
            var allBbs = Members.Select(f => f.BoundingBox).Where(bb => bb.Width > 0).ToList();
            if (allBbs.Count == 0) return $"{paths} paths · {pts} pts";
            double minX = allBbs.Min(bb => bb.X);
            double minY = allBbs.Min(bb => bb.Y);
            double maxX = allBbs.Max(bb => bb.X + bb.Width);
            double maxY = allBbs.Max(bb => bb.Y + bb.Height);
            return $"{paths} paths · {pts} pts  {maxX - minX:F0}×{maxY - minY:F0} mm";
        }
    }

    public Guid? GroupId => Members[0].GroupId;

    public FileGroupItem(IReadOnlyList<ImportedFile> members)
    {
        Members = members;
        // Forward property changes from the underlying file
        foreach (var m in members)
            m.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(ImportedFile.Visible) or nameof(ImportedFile.DisplayName))
                    Notify(e.PropertyName!);
            };
    }
}
