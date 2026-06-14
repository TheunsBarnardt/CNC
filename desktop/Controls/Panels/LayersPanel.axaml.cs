using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Backend.Models;
using Desktop.ViewModels;
using Desktop.Views;

namespace Desktop.Controls.Panels;

public partial class LayersPanel : UserControl
{
    private MainViewModel? _vm;
    private MainViewModel? Vm => _vm;

    public LayersPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextSet;
        Loaded += OnLoaded;
    }

    private void BindToVm(MainViewModel vm)
    {
        _vm = vm;
        if (LayerTree is not null)
        {
            LayerTree.ItemsSource = vm.LayerTree;
        }

        if (NoGoZonesList is not null)
        {
            NoGoZonesList.ItemsSource = vm.NoGoZones;
            ZonesEmptyHint.IsVisible = vm.NoGoZones.Count == 0;
            vm.NoGoZones.CollectionChanged += (_, _) =>
                ZonesEmptyHint.IsVisible = _vm?.NoGoZones.Count == 0;
        }
    }

    private void OnDataContextSet(object? s, EventArgs e)
    {
        if (DataContext is MainViewModel vm) BindToVm(vm);
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm) BindToVm(vm);
    }

    // ── Tree row interactions ─────────────────────────────────────────────

    private void OnTreeRowPressed(object? s, PointerPressedEventArgs e)
    {
        if (Vm is null || s is not Border border || border.Tag is not LayerTreeItem node) return;

        switch (node.Kind)
        {
            case LayerTreeNodeKind.Layer when node.Layer is not null:
                Vm.SetActiveLayer(node.Layer.Id);
                break;

            case LayerTreeNodeKind.Group:
                Vm.SelectTreeGroup(node);
                break;

            case LayerTreeNodeKind.Part:
                Vm.SelectTreePart(node);
                break;

            case LayerTreeNodeKind.Object:
                // Clicking an object node selects its parent Part on the canvas
                Vm.SelectTreePart(node);
                break;
        }
    }

    private void OnExpandToggle(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is LayerTreeItem node)
            Vm?.ToggleTreeNodeExpanded(node);
        e.Handled = true; // prevent row PointerPressed from also firing
    }

    // ── Layer-node handlers ───────────────────────────────────────────────

    private void OnAddLayer(object? s, RoutedEventArgs e) => Vm?.AddLayer();

    private async void OnChangeColor(object? s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not LayerTreeItem node || node.Layer is null) return;
        var dlg = new ColorPickerDialog { SelectedColor = node.Layer.Color };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;
        if (await dlg.ShowDialog<bool>(owner))
            Vm?.UpdateLayer(node.Layer, color: dlg.SelectedColor);
    }

    private void OnLayerNameCommit(object? s, RoutedEventArgs e)
    {
        if (s is TextBox tb && tb.Tag is LayerTreeItem node && node.Layer is not null)
            Vm?.UpdateLayer(node.Layer, name: tb.Text?.Trim() ?? node.Layer.Name);
    }

    private void OnLayerNameKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && s is TextBox tb && tb.Tag is LayerTreeItem node && node.Layer is not null)
        {
            Vm?.UpdateLayer(node.Layer, name: tb.Text?.Trim() ?? node.Layer.Name);
            e.Handled = true;
        }
    }

    private void OnCycleOperationMode(object? s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not LayerTreeItem node || node.Layer is null) return;
        var modes = Enum.GetValues<LayerOperationMode>();
        int next = ((int)node.Layer.OperationMode + 1) % modes.Length;
        Vm?.UpdateLayer(node.Layer, operationMode: modes[next]);
    }

    private void OnToggleVisible(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is LayerTreeItem node)
            Vm?.ToggleTreeVisible(node);
    }

    private void OnToggleLock(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is LayerTreeItem node && node.Layer is not null)
            Vm?.UpdateLayer(node.Layer, locked: !node.Layer.Locked);
    }

    private void OnDeleteLayer(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is LayerTreeItem node && node.Layer is not null)
            Vm?.DeleteLayer(node.Layer);
    }

    private void OnFeedOverrideCommit(object? s, RoutedEventArgs e)
    {
        if (s is not TextBox tb || tb.Tag is not LayerTreeItem node || node.Layer is null) return;
        var v = string.IsNullOrWhiteSpace(tb.Text) ? (double?)null
              : double.TryParse(tb.Text, out var d) ? d : node.Layer.FeedRateMmMinOverride;
        Vm?.SetLayerFeedOverride(node.Layer, v);
    }

    private void OnPowerOverrideCommit(object? s, RoutedEventArgs e)
    {
        if (s is not TextBox tb || tb.Tag is not LayerTreeItem node || node.Layer is null) return;
        var v = string.IsNullOrWhiteSpace(tb.Text) ? (double?)null
              : double.TryParse(tb.Text, out var d) ? Math.Clamp(d, 0, 100) : node.Layer.LaserPowerPercentOverride;
        Vm?.SetLayerPowerOverride(node.Layer, v);
    }

    private void OnOverrideKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || s is not TextBox tb) return;
        TopLevel.GetTopLevel(tb)?.Focus();
        e.Handled = true;
    }

    // ── Layer re-ordering ─────────────────────────────────────────────────

    private void OnMoveLayerUp(object? s, RoutedEventArgs e) => Vm?.MoveActiveLayerUp();
    private void OnMoveLayerDown(object? s, RoutedEventArgs e) => Vm?.MoveActiveLayerDown();

    // ── Group-node handlers ───────────────────────────────────────────────

    private void OnGroupSelected(object? s, RoutedEventArgs e) => Vm?.GroupSelectedParts();

    private void OnGroupNameCommit(object? s, RoutedEventArgs e)
    {
        if (s is TextBox tb && tb.Tag is LayerTreeItem node && node.Kind == LayerTreeNodeKind.Group)
            Vm?.RenameTreeGroup(node, tb.Text ?? "");
    }

    private void OnGroupNameKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && s is TextBox tb && tb.Tag is LayerTreeItem node)
        {
            Vm?.RenameTreeGroup(node, tb.Text ?? "");
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) e.Handled = true;
    }

    private void OnUngroup(object? s, RoutedEventArgs e)
    {
        var node = TagFromSender<LayerTreeItem>(s);
        if (node is not null) Vm?.UngroupTreeNode(node);
    }

    // ── Context menu handlers ─────────────────────────────────────────────

    private void OnCtxDuplicate(object? s, RoutedEventArgs e)
    {
        var node = TagFromSender<LayerTreeItem>(s);
        if (node?.Part is not null) Vm?.DuplicatePart(node.Part);
    }

    private void OnCtxDelete(object? s, RoutedEventArgs e)
    {
        var node = TagFromSender<LayerTreeItem>(s);
        if (node?.Part is not null) Vm?.DeletePart(node.Part);
    }

    private async void OnCtxMoveToLayer(object? s, RoutedEventArgs e)
    {
        var node = TagFromSender<LayerTreeItem>(s);
        if (node?.Part is null || Vm is null) return;

        var layers = Vm.AllLayers;
        var menu = new ContextMenu();
        foreach (var layer in layers)
        {
            var item = new MenuItem { Header = layer.Name, Tag = layer };
            item.Click += (_, _) => Vm.MovePartToLayerById(node.Part, layer.Id);
            menu.Items.Add(item);
        }
        var anchor = TopLevel.GetTopLevel(this) as Control ?? this;
        menu.Open(anchor);
    }

    private void OnCtxGroup(object? s, RoutedEventArgs e)
    {
        var node = TagFromSender<LayerTreeItem>(s);
        if (node?.Part is not null)
        {
            // Select this part then group
            Vm?.SelectTreePart(node);
            Vm?.GroupSelectedParts();
        }
    }

    private void OnCtxPopFromGroup(object? s, RoutedEventArgs e)
    {
        var node = TagFromSender<LayerTreeItem>(s);
        if (node?.Part is not null)
        {
            node.Part.GroupId = null;
            Vm?.RebuildLayerTreePublic();
        }
    }

    private void OnCtxUngroupFromPart(object? s, RoutedEventArgs e)
    {
        // Ungroup the whole group that contains this part
        var node = TagFromSender<LayerTreeItem>(s);
        if (node?.Part?.GroupId is { } gid)
        {
            // Build a synthetic group node and delegate to existing ungroup logic
            var groupNode = new LayerTreeItem
            {
                Kind    = LayerTreeNodeKind.Group,
                GroupId = gid,
            };
            Vm?.UngroupTreeNode(groupNode);
        }
    }

    /// <summary>Extracts the Tag from either a MenuItem or Button sender.</summary>
    private static T? TagFromSender<T>(object? sender) where T : class
    {
        return sender switch
        {
            MenuItem mi => mi.Tag as T,
            Button btn  => btn.Tag as T,
            _           => null,
        };
    }

    // ── No-go zones ───────────────────────────────────────────────────────

    private void OnAddNoGoZone(object? s, RoutedEventArgs e) => Vm?.AddNoGoZone();

    private void OnDeleteNoGoZone(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is NoGoZone zone)
            Vm?.DeleteNoGoZone(zone);
    }

    private void OnZoneNameCommit(object? s, RoutedEventArgs e)
    {
        if (s is TextBox tb && tb.Tag is NoGoZone zone && !string.IsNullOrWhiteSpace(tb.Text))
            zone.Label = tb.Text.Trim();
    }

    private void OnZoneDimsCommit(object? s, RoutedEventArgs e) => Vm?.RefreshNoGoZones();

    private void OnZoneKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || s is not TextBox tb) return;
        TopLevel.GetTopLevel(tb)?.Focus();
        e.Handled = true;
    }
}
