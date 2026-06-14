using Avalonia.Controls;
using Avalonia.Interactivity;
using Desktop.ViewModels;

namespace Desktop.Controls.Toolbars;

public partial class NodeEditToolbar : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public NodeEditToolbar()
    {
        InitializeComponent();
    }

    /// <summary>Called by MainWindow when viewport fires NodeSelected.</summary>
    public void OnNodeSelected(double? x, double? y)
    {
        bool hasNode = x.HasValue && y.HasValue;
        TbNodeX.Text       = hasNode ? x!.Value.ToString("F3") : "";
        TbNodeY.Text       = hasNode ? y!.Value.ToString("F3") : "";
        TbNodeX.IsEnabled  = hasNode;
        TbNodeY.IsEnabled  = hasNode;
    }

    // ── X/Y position editing ──────────────────────────────────────────────

    private void OnNodeXChanged(object? s, RoutedEventArgs e)
    {
        if (Vm is null || string.IsNullOrWhiteSpace(TbNodeX.Text)) return;
        Vm.StatusText = "Node X move: node position editing coming in a future task";
    }

    private void OnNodeYChanged(object? s, RoutedEventArgs e)
    {
        if (Vm is null || string.IsNullOrWhiteSpace(TbNodeY.Text)) return;
        Vm.StatusText = "Node Y move: node position editing coming in a future task";
    }

    // ── Node alignment ────────────────────────────────────────────────────

    private void OnNodeAlignLeft   (object? s, RoutedEventArgs e) => Vm?.AlignNodesLeft();
    private void OnNodeAlignHCenter(object? s, RoutedEventArgs e) => Vm?.AlignNodesHCenter();
    private void OnNodeAlignRight  (object? s, RoutedEventArgs e) => Vm?.AlignNodesRight();
    private void OnNodeAlignTop    (object? s, RoutedEventArgs e) => Vm?.AlignNodesTop();
    private void OnNodeAlignVCenter(object? s, RoutedEventArgs e) => Vm?.AlignNodesVCenter();
    private void OnNodeAlignBottom (object? s, RoutedEventArgs e) => Vm?.AlignNodesBottom();

    // ── Node type ─────────────────────────────────────────────────────────

    private void OnNodeSmooth    (object? s, RoutedEventArgs e) { SetActiveNodeBtn(BtnNodeSmooth);     Vm?.SetNodeTypeSmoothSym(); }
    private void OnNodeSmoothAsym(object? s, RoutedEventArgs e) { SetActiveNodeBtn(BtnNodeSmoothAsym); Vm?.SetNodeTypeSmoothAsym(); }
    private void OnNodeCorner    (object? s, RoutedEventArgs e) { SetActiveNodeBtn(BtnNodeCorner);     Vm?.SetNodeTypeCorner(); }
    private void OnNodeCusp      (object? s, RoutedEventArgs e) { SetActiveNodeBtn(BtnNodeCusp);       Vm?.SetNodeTypeCusp(); }

    private void SetActiveNodeBtn(Button active)
    {
        foreach (var b in new[] { BtnNodeSmooth, BtnNodeSmoothAsym, BtnNodeCorner, BtnNodeCusp })
        {
            if (b.Classes.Contains("active")) b.Classes.Remove("active");
        }
        if (!active.Classes.Contains("active")) active.Classes.Add("active");
    }

    // ── Extra tools ───────────────────────────────────────────────────────

    private void OnNodeTransform(object? s, RoutedEventArgs e) => Vm?.ScaleSelectedNodes();
    private void OnNodeAdd      (object? s, RoutedEventArgs e) => Vm?.AddOrJoinNode();
    private void OnSimplify     (object? s, RoutedEventArgs e) => Vm?.SimplifyPath();
    private void OnScissors     (object? s, RoutedEventArgs e) => Vm?.SplitPathAtNode();

    // ── Done ──────────────────────────────────────────────────────────────

    private void OnDone(object? s, RoutedEventArgs e) => Vm?.ExitNodeEditMode();
}
