using System.ComponentModel;
using Avalonia;
using Backend.Models;

namespace Desktop.ViewModels;

public enum LayerTreeNodeKind { Layer, Group, Part, Object }

/// <summary>
/// One row in the flat layer-tree list. Depth drives the left-margin indent.
/// Layers sit at depth 0; ungrouped parts and group-headers at depth 1;
/// parts inside a group at depth 2; path objects at part.Depth+1.
/// </summary>
public sealed class LayerTreeItem : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify(string p) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

    public LayerTreeNodeKind Kind { get; init; }

    // --- Layer node ---
    public Layer? Layer { get; init; }

    // --- Group node ---
    public Guid GroupId { get; init; }
    private string _groupName = "Group";
    public string GroupName
    {
        get => _groupName;
        set { if (_groupName == value) return; _groupName = value; Notify(nameof(GroupName)); Notify(nameof(DisplayName)); }
    }

    // --- Part node ---
    public Part? Part { get; init; }
    public ImportedFile? File { get; init; }   // the file backing this Part
    public string PartDisplayName { get; init; } = "";

    // --- Object node (individual PathGeometry) ---
    public PathGeometry? PathObject { get; init; }
    public int PathIndex { get; init; }         // 1-based index for default label

    // --- Common display ---
    public string DisplayName => Kind switch
    {
        LayerTreeNodeKind.Layer  => Layer?.Name ?? "",
        LayerTreeNodeKind.Group  => GroupName,
        LayerTreeNodeKind.Part   => PartDisplayName,
        LayerTreeNodeKind.Object => PathObject?.Label ?? $"Path {PathIndex}",
        _ => ""
    };

    public int Depth { get; init; }
    public Thickness IndentMargin => new(Depth * 18.0, 0, 0, 0);

    // Layer colour swatch
    public string LayerColor => Layer?.Color ?? "#3b82f6";

    // Expand / collapse (layers, groups, and parts)
    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded == value) return; _isExpanded = value; Notify(nameof(IsExpanded)); Notify(nameof(ExpandIcon)); }
    }
    // "▼" when expanded, "▶" when collapsed
    public string ExpandIcon => _isExpanded ? "▼" : "▶";

    // Canvas-selection highlight
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; Notify(nameof(IsSelected)); }
    }

    // Visibility (mirrors the underlying model object)
    private bool _visible = true;
    public bool Visible
    {
        get => _visible;
        set { if (_visible == value) return; _visible = value; Notify(nameof(Visible)); }
    }

    // Convenience type discriminators for XAML
    public bool IsLayerKind  => Kind == LayerTreeNodeKind.Layer;
    public bool IsGroupKind  => Kind == LayerTreeNodeKind.Group;
    public bool IsPartKind   => Kind == LayerTreeNodeKind.Part;
    public bool IsObjectKind => Kind == LayerTreeNodeKind.Object;
    /// <summary>Layers, groups, and parts are all expandable; Objects are leaf nodes.</summary>
    public bool IsExpandable => Kind != LayerTreeNodeKind.Object;
    /// <summary>True for Part nodes that belong to a group (move together on canvas).</summary>
    public bool IsInGroup    => Kind == LayerTreeNodeKind.Part && Part?.GroupId != null;
    /// <summary>Show a link icon when the part moves with siblings.</summary>
    public bool ShowLinkIcon => IsInGroup;
}
