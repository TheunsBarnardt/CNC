using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Backend.Models;
using Desktop.ViewModels;
using Desktop.Views;

namespace Desktop.Controls.Toolbars;

public partial class EditToolbar : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;
    private bool _loading;
    private bool _lockAspect;
    private string _cornerStyle = "miter";

    public EditToolbar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeToVm();
        SetCornerActive(BtnCornerMiter);
    }

    private void SubscribeToVm()
    {
        if (Vm is null) return;
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPart))
                LoadFromPart(Vm.SelectedPart);
            if (e.PropertyName == nameof(MainViewModel.Layers))
                RefreshLayers();
        };
        BtnArray.Click += OnArray;
        RefreshLayers();
        LoadFromPart(Vm?.SelectedPart);
    }

    private void RefreshLayers()
    {
        if (Vm is null) return;
        CbLayer.Items?.Clear();
        foreach (var lyr in Vm.AllLayers)
            CbLayer.Items?.Add(lyr);
        if (Vm.SelectedPart is { } part && Vm.AllLayers.FirstOrDefault(l => l.Id == part.LayerId) is { } sel)
            CbLayer.SelectedItem = sel;
    }

    private void LoadFromPart(Part? part)
    {
        if (part is null) return;
        _loading = true;
        TbX.Text   = part.X.ToString("F2");
        TbY.Text   = part.Y.ToString("F2");
        TbRot.Text = part.RotationDeg.ToString("F1");
        if (Vm?.FileById(part.FileId) is { } file)
        {
            var bb = file.BoundingBox;
            TbW.Text = (bb.Width  * part.ScaleX).ToString("F2");
            TbH.Text = (bb.Height * part.ScaleY).ToString("F2");
        }
        TbTabCount.Text = part.TabCount.ToString();
        TbTabWidth.Text = part.TabWidthMm.ToString("F1");
        UpdateTabHint(part);
        _loading = false;
    }

    private void UpdateTabHint(Part? part)
    {
        if (TabHint is null || part is null) return;
        TabHint.Text = part.TabCount <= 0
            ? "No tabs — part will fall free"
            : $"{part.TabCount} tab{(part.TabCount == 1 ? "" : "s")} · {part.TabWidthMm:F1} mm each";
    }

    // ── Transform inputs ──────────────────────────────────────────────────

    private void OnTransformChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part) return;
        if (double.TryParse(TbX.Text, out var x) && double.TryParse(TbY.Text, out var y))
            Vm.CommitPartTransform(part, x, y, part.RotationDeg, part.ScaleX, part.ScaleY);
    }

    private void OnSizeChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part) return;
        if (Vm.FileById(part.FileId) is not { } file) return;
        var bb = file.BoundingBox;
        if (bb.Width == 0 || bb.Height == 0) return;
        if (double.TryParse(TbW.Text, out var w) && double.TryParse(TbH.Text, out var h))
        {
            if (_lockAspect)
            {
                if (s == TbW) h = w / (bb.Width / bb.Height);
                else          w = h * (bb.Width / bb.Height);
                _loading = true;
                TbW.Text = w.ToString("F2");
                TbH.Text = h.ToString("F2");
                _loading = false;
            }
            Vm.CommitPartTransform(part, part.X, part.Y, part.RotationDeg, w / bb.Width, h / bb.Height);
        }
    }

    private void OnRotChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part) return;
        if (double.TryParse(TbRot.Text, out var r))
            Vm.CommitPartTransform(part, part.X, part.Y, r, part.ScaleX, part.ScaleY);
    }

    private void OnToggleLock(object? s, RoutedEventArgs e)
    {
        _lockAspect = !_lockAspect;
        BtnLock.Foreground = _lockAspect ? Avalonia.Media.Brushes.DodgerBlue : null;
    }

    // ── Arrange ───────────────────────────────────────────────────────────

    private void OnBringToFront(object? s, RoutedEventArgs e) => Vm?.BringToFront();
    private void OnSendToBack  (object? s, RoutedEventArgs e) => Vm?.SendToBack();
    private void OnBringForward(object? s, RoutedEventArgs e) => Vm?.BringForward();
    private void OnSendBackward(object? s, RoutedEventArgs e) => Vm?.SendBackward();

    // ── Align ─────────────────────────────────────────────────────────────

    private void OnAlignLeft   (object? s, RoutedEventArgs e) => AlignEdge("left");
    private void OnAlignRight  (object? s, RoutedEventArgs e) => AlignEdge("right");
    private void OnAlignTop    (object? s, RoutedEventArgs e) => AlignEdge("top");
    private void OnAlignBottom (object? s, RoutedEventArgs e) => AlignEdge("bottom");
    private void OnCenter      (object? s, RoutedEventArgs e) => AlignEdge("center");
    private void OnAlignHCenter(object? s, RoutedEventArgs e) => Vm?.AlignHCenter();
    private void OnAlignVCenter(object? s, RoutedEventArgs e) => Vm?.AlignVCenter();

    private void AlignEdge(string edge)
    {
        if (Vm?.SelectedPart is not { } part) return;
        var proj = Vm.Project;
        double sw = proj.TableWidthMm, sh = proj.TableHeightMm;
        if (Vm.FileById(part.FileId) is not { } file) return;
        var bb = file.BoundingBox;
        double w = bb.Width * Math.Abs(part.ScaleX), h = bb.Height * Math.Abs(part.ScaleY);
        (double nx, double ny) = edge switch
        {
            "left"   => (0,          part.Y),
            "right"  => (sw - w,     part.Y),
            "top"    => (part.X,     0),
            "bottom" => (part.X,     sh - h),
            "center" => ((sw - w)/2, (sh - h)/2),
            _        => (part.X, part.Y),
        };
        Vm.CommitPartTransform(part, nx, ny, part.RotationDeg, part.ScaleX, part.ScaleY);
        LoadFromPart(part);
    }

    // ── Reflect ───────────────────────────────────────────────────────────

    private void OnReflectH(object? s, RoutedEventArgs e) => Vm?.ReflectH();
    private void OnReflectV(object? s, RoutedEventArgs e) => Vm?.ReflectV();

    // ── Edit (node editing) ───────────────────────────────────────────────

    private void OnEnterNodeEdit(object? s, RoutedEventArgs e) => Vm?.EnterNodeEditMode();

    // ── Offset flyout ─────────────────────────────────────────────────────

    private void OnOffsetDirectionChanged(object? s, RoutedEventArgs e)
    {
        if (s == CbExternal && CbExternal.IsChecked == true)
            CbInner.IsChecked = false;
        else if (s == CbInner && CbInner.IsChecked == true)
            CbExternal.IsChecked = false;
        else
            CbExternal.IsChecked = true; // at least one must be checked
    }

    private void OnCornerMiter(object? s, RoutedEventArgs e) { _cornerStyle = "miter"; SetCornerActive(BtnCornerMiter); }
    private void OnCornerRound(object? s, RoutedEventArgs e) { _cornerStyle = "round"; SetCornerActive(BtnCornerRound); }
    private void OnCornerBevel(object? s, RoutedEventArgs e) { _cornerStyle = "bevel"; SetCornerActive(BtnCornerBevel); }

    private void SetCornerActive(Button active)
    {
        if (BtnCornerMiter is null) return; // called from ctor before InitializeComponent
        foreach (var b in new[] { BtnCornerMiter, BtnCornerRound, BtnCornerBevel })
        {
            if (b.Classes.Contains("active")) b.Classes.Remove("active");
        }
        if (!active.Classes.Contains("active")) active.Classes.Add("active");
    }

    private void OnOffsetSliderChanged(object? s, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (TbOffsetDist is not null)
            TbOffsetDist.Text = SliderOffset.Value.ToString("F1");
    }

    private void OnOffsetDistChanged(object? s, RoutedEventArgs e)
    {
        if (double.TryParse(TbOffsetDist.Text, out var v))
            SliderOffset.Value = Math.Clamp(v, 0.1, 50);
    }

    private void OnOffsetCancel(object? s, RoutedEventArgs e) => BtnOffset.Flyout?.Hide();

    private void OnOffsetConfirm(object? s, RoutedEventArgs e)
    {
        BtnOffset.Flyout?.Hide();
        if (!double.TryParse(TbOffsetDist.Text, out var dist) || dist <= 0) return;
        bool external  = CbExternal.IsChecked ?? true;
        bool outerOnly = CbOuterOnly.IsChecked ?? false;
        Vm?.ApplyOffset(dist, external, _cornerStyle, outerOnly);
    }

    // ── Tabs ──────────────────────────────────────────────────────────────

    private void OnTabCountChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part) return;
        if (int.TryParse(TbTabCount.Text, out var n) && n >= 0)
        {
            part.TabCount = n;
            UpdateTabHint(part);
        }
    }

    private void OnTabWidthChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part) return;
        if (double.TryParse(TbTabWidth.Text, out var w) && w > 0)
        {
            part.TabWidthMm = w;
            UpdateTabHint(part);
        }
    }

    private void OnTabKeyDown(object? s, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter || s is not TextBox tb) return;
        TopLevel.GetTopLevel(tb)?.Focus();
        e.Handled = true;
    }

    // ── Layer ──────────────────────────────────────────────────────────────

    private void OnLayerChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part || CbLayer.SelectedItem is not Layer layer) return;
        part.LayerId = layer.Id;
    }

    // ── Array / Duplicate / Delete ────────────────────────────────────────

    private async void OnArray(object? s, RoutedEventArgs e)
    {
        var dlg = new ArrayPanel();
        var owner = this.FindAncestorOfType<Window>();
        if (owner is not null && await dlg.ShowDialog<bool>(owner))
            Vm?.CreateArray(dlg.Type, dlg);
    }

    private void OnDuplicate(object? s, RoutedEventArgs e) => Vm?.DuplicateSelected();
    private void OnDelete   (object? s, RoutedEventArgs e) => Vm?.DeleteSelected();
}
