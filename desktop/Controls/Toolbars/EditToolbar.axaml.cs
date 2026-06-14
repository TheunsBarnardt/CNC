using Avalonia.Controls;
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

    public EditToolbar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SubscribeToVm();
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
        if (Vm.SelectedPart is { } part && Vm.AllLayers.FirstOrDefault(l => l.Id == part.LayerId) is { } selectedLayer)
            CbLayer.SelectedItem = selectedLayer;
    }

    private void LoadFromPart(Part? part)
    {
        if (part is null) return;
        _loading = true;
        TbX.Text   = part.X.ToString("F2");
        TbY.Text   = part.Y.ToString("F2");
        TbRot.Text = part.RotationDeg.ToString("F1");
        // W/H: use bounding box of the file geometry if available
        if (Vm?.FileById(part.FileId) is { } file)
        {
            var bb = file.BoundingBox;
            TbW.Text = (bb.Width  * part.ScaleX).ToString("F2");
            TbH.Text = (bb.Height * part.ScaleY).ToString("F2");
        }
        _loading = false;
    }

    private void OnTransformChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part) return;
        if (double.TryParse(TbX.Text, out var x) &&
            double.TryParse(TbY.Text, out var y))
            Vm.CommitPartTransform(part, x, y, part.RotationDeg, part.ScaleX, part.ScaleY);
    }

    private void OnSizeChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part) return;
        if (Vm.FileById(part.FileId) is not { } file) return;
        var bb = file.BoundingBox;
        if (bb.Width == 0 || bb.Height == 0) return;
        if (double.TryParse(TbW.Text, out var w) &&
            double.TryParse(TbH.Text, out var h))
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
            Vm.CommitPartTransform(part, part.X, part.Y, part.RotationDeg,
                w / bb.Width, h / bb.Height);
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
        BtnLock.Foreground = _lockAspect
            ? Avalonia.Media.Brushes.DodgerBlue
            : null;
    }

    private void OnMirrorH(object? s, RoutedEventArgs e)
    {
        if (Vm?.SelectedPart is not { } p) return;
        Vm.CommitPartTransform(p, p.X, p.Y, p.RotationDeg, -p.ScaleX, p.ScaleY);
    }

    private void OnMirrorV(object? s, RoutedEventArgs e)
    {
        if (Vm?.SelectedPart is not { } p) return;
        Vm.CommitPartTransform(p, p.X, p.Y, p.RotationDeg, p.ScaleX, -p.ScaleY);
    }

    private void OnAlignLeft  (object? s, RoutedEventArgs e) => AlignEdge("left");
    private void OnAlignRight (object? s, RoutedEventArgs e) => AlignEdge("right");
    private void OnAlignTop   (object? s, RoutedEventArgs e) => AlignEdge("top");
    private void OnAlignBottom(object? s, RoutedEventArgs e) => AlignEdge("bottom");
    private void OnCenter     (object? s, RoutedEventArgs e) => AlignEdge("center");

    private void AlignEdge(string edge)
    {
        if (Vm?.SelectedPart is not { } part) return;
        var proj = Vm.Project;
        double sw = proj.TableWidthMm, sh = proj.TableHeightMm;
        if (Vm.FileById(part.FileId) is not { } file) return;
        var bb  = file.BoundingBox;
        double w = bb.Width * part.ScaleX, h = bb.Height * part.ScaleY;
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

    private void OnRotate90CW(object? s, RoutedEventArgs e)
    {
        if (Vm?.SelectedPart is not { } p) return;
        double newRot = (p.RotationDeg + 90) % 360;
        Vm.CommitPartTransform(p, p.X, p.Y, newRot, p.ScaleX, p.ScaleY);
        TbRot.Text = newRot.ToString("F1");
    }

    private void OnRotate90CCW(object? s, RoutedEventArgs e)
    {
        if (Vm?.SelectedPart is not { } p) return;
        double newRot = (p.RotationDeg - 90 + 360) % 360;
        Vm.CommitPartTransform(p, p.X, p.Y, newRot, p.ScaleX, p.ScaleY);
        TbRot.Text = newRot.ToString("F1");
    }

    private void OnBringToFront(object? s, RoutedEventArgs e) => Vm?.BringToFront();

    private void OnSendToBack(object? s, RoutedEventArgs e) => Vm?.SendToBack();

    private void OnLayerChanged(object? s, RoutedEventArgs e)
    {
        if (_loading || Vm?.SelectedPart is not { } part || CbLayer.SelectedItem is not Layer layer) return;
        part.LayerId = layer.Id;
    }

    private void OnApplyOffset(object? s, RoutedEventArgs e)
    {
        if (Vm?.SelectedPart is not { } part) return;
        if (!double.TryParse(TbOffset.Text, out var offsetMm)) return;
        // Note: full offset implementation requires Clipper2; deferred for now
        Vm.StatusText = $"Contour offset not yet implemented (requested: {offsetMm:F2}mm)";
    }

    private async void OnArray(object? s, RoutedEventArgs e)
    {
        var dlg = new ArrayPanel();
        var owner = this.FindAncestorOfType<Window>();
        if (owner is not null && await dlg.ShowDialog<bool>(owner))
        {
            Vm?.CreateArray(dlg.Type, dlg);
        }
    }

    private void OnDuplicate(object? s, RoutedEventArgs e) => Vm?.DuplicateSelected();
    private void OnDelete   (object? s, RoutedEventArgs e) => Vm?.DeleteSelected();
}
