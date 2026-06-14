using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Backend.Services;
using Desktop.Controls;
using Desktop.Controls.Toolbars;
using Desktop.ViewModels;
using Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.Views;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        _vm = vm;

        // Viewport: needs the VM to draw parts + guidelines
        Viewport.Attach(vm);

        // Canvas selection / drag → drive the view-model (so the edit toolbar,
        // part inspector and persistence all track what's selected on the sheet).
        Viewport.SelectionChanged += id =>
            vm.SelectedPart = id is null
                ? null
                : vm.Project.Parts.FirstOrDefault(p => p.Id == id.Value);
        Viewport.PartCommitted += part =>
            vm.CommitPartTransform(part, part.X, part.Y, part.RotationDeg, part.ScaleX, part.ScaleY);
        Viewport.TransformStarted += () => vm.Checkpoint();

        // Guide events
        Viewport.GuideCreateRequested += (x, y, angleDeg) =>
            vm.AddGuide(new Backend.Models.Guide { X = x, Y = y, AngleDeg = angleDeg });
        Viewport.GuideMoved    += guide => vm.UpdateGuide(guide);
        Viewport.GuideEditRequested += async guide =>
        {
            var dlg = new GuideDialog();
            dlg.Load(guide, vm);
            await dlg.ShowDialog(this);
        };

        // SimBar needs to hook into VM.InSimMode to rebuild the simulation
        SimBar.DataContext      = vm;
        EditBar.DataContext     = vm;
        NodeEditBar.DataContext = vm;

        // All panels inherit DataContext from the window automatically,
        // but set explicitly to be safe
        PnlFiles.DataContext   = vm;
        PnlLayers.DataContext  = vm;
        PnlCut.DataContext     = vm;
        PnlNest.DataContext    = vm;
        PnlGcode.DataContext   = vm;
        PnlDevice.DataContext  = vm;

        // Sync initial canvas display state from the VM into the viewport
        Viewport.SetDarkCanvas(vm.DarkCanvas);
        Viewport.SetShowGrid(vm.ShowGrid);
        SetToggle(BtnCanvasDark, vm.DarkCanvas);
        SetToggle(BtnGridToggle, vm.ShowGrid);
        SetToggle(BtnSnapToggle, false);

        // Default-select the Files tab
        SelectTab(TabFiles, PnlFiles);

        // Header buttons
        BtnNew.Click      += (_, _)       => vm.NewProject();
        BtnOpen.Click     += async (_, _) => await vm.LoadAsync(StorageProvider);
        BtnSave.Click     += async (_, _) => await vm.SaveAsync(StorageProvider);
        BtnGcode.Click    += async (_, _) => await vm.GenerateGcodeAsync(StorageProvider);
        BtnSimMode.Click  += (_, _)       => vm.EnterSimMode();
        BtnTemplates.Click += (_, _)      => OpenTemplates();
        BtnSettings.Click  += (_, _)      => OpenSettings();

        // Left tool-rail import
        BtnImport.Click += async (_, _) => await vm.ImportAsync(StorageProvider);

        // Shape tools
        BtnLine.Click += (_, _) =>
        {
            vm.CreateLine(100);
            Viewport.FitToView();
        };
        BtnRectangle.Click += async (_, _) =>
        {
            var dlg = new ShapeDialog { Title = "Create Rectangle" };
            if (await dlg.ShowDialog<bool>(this))
            {
                vm.CreateRectangle(dlg.Width_mm, dlg.Height_mm, dlg.Radius_mm);
                Viewport.FitToView();
            }
        };
        BtnCircle.Click += async (_, _) =>
        {
            var dlg = new CircleDialog();
            if (await dlg.ShowDialog<bool>(this))
            {
                vm.CreateCircle(dlg.Radius_mm);
                Viewport.FitToView();
            }
        };
        BtnEllipse.Click += async (_, _) =>
        {
            var dlg = new ShapeDialog { Title = "Create Ellipse" };
            if (await dlg.ShowDialog<bool>(this))
            {
                vm.CreateEllipse(dlg.Width_mm, dlg.Height_mm);
                Viewport.FitToView();
            }
        };
        BtnPolygon.Click += async (_, _) =>
        {
            var dlg = new PolygonDialog();
            if (await dlg.ShowDialog<bool>(this))
            {
                vm.CreatePolygon(dlg.SideCount, dlg.Radius_mm);
                Viewport.FitToView();
            }
        };
        BtnStar.Click += async (_, _) =>
        {
            var dlg = new StarDialog();
            if (await dlg.ShowDialog<bool>(this))
            {
                vm.CreateStar(dlg.PointCount, dlg.OuterRadius_mm, dlg.InnerRadius_mm);
                Viewport.FitToView();
            }
        };
        BtnPen.Click += (_, _) => vm.ActivatePenTool();

        // Tab switching
        TabFiles.Click  += (_, _) => SelectTab(TabFiles,  PnlFiles);
        TabLayers.Click += (_, _) => SelectTab(TabLayers, PnlLayers);
        TabCut.Click    += (_, _) => SelectTab(TabCut,    PnlCut);
        TabNest.Click   += (_, _) => SelectTab(TabNest,   PnlNest);
        TabGcode.Click  += (_, _) => SelectTab(TabGcode,  PnlGcode);
        TabDevice.Click += (_, _) => SelectTab(TabDevice, PnlDevice);

        // Simulation: sim time changes → viewport redraw
        SimBar.SimTimeChanged += (_, state) =>
        {
            Viewport.SimState = state;
            Viewport.InvalidateVisual();
        };

        // Drive the contextual bottom toolbars from VM state (deterministic).
        UpdateToolbars();
        vm.PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(vm.InSimMode):
                    Viewport.InvalidateVisual();
                    UpdateToolbars();
                    break;
                case nameof(vm.SelectedPart):
                case nameof(vm.NodeEditMode):
                    UpdateToolbars();
                    break;
                case nameof(vm.PenToolActive):
                    SetActive(BtnPen, vm.PenToolActive);
                    break;
            }
        };

        // Drag-drop onto viewport
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, (s, ea) => ea.DragEffects = DragDropEffects.Copy);
        DragDrop.SetAllowDrop(Viewport, true);

        // Keyboard shortcuts
        KeyDown += OnKeyDown;
    }

    // ── tab switching ─────────────────────────────────────────────────────

    private void SelectTab(Button tab, Control target)
    {
        ShowPanel(target);
        foreach (var b in new[] { TabFiles, TabLayers, TabCut, TabNest, TabGcode, TabDevice })
            SetActive(b, b == tab);
    }

    private void ShowPanel(Control target)
    {
        PnlFiles.IsVisible  = target == PnlFiles;
        PnlLayers.IsVisible = target == PnlLayers;
        PnlCut.IsVisible    = target == PnlCut;
        PnlNest.IsVisible   = target == PnlNest;
        PnlGcode.IsVisible  = target == PnlGcode;
        PnlDevice.IsVisible = target == PnlDevice;
    }

    // ── small helpers for the ".active" visual state class ────────────────

    private static void SetActive(Button b, bool active)
    {
        if (active) { if (!b.Classes.Contains("active")) b.Classes.Add("active"); }
        else        { b.Classes.Remove("active"); }
    }

    private static void SetToggle(Button b, bool on) => SetActive(b, on);

    /// <summary>Shows exactly the one contextual bottom toolbar that applies to
    /// the current mode (sim / node-edit / part-selected), or none.</summary>
    private void UpdateToolbars()
    {
        if (_vm is null) return;
        bool sim  = _vm.InSimMode;
        bool node = _vm.NodeEditMode;
        SimBar.IsVisible      = sim;
        NodeEditBar.IsVisible = !sim && node;
        EditBar.IsVisible     = !sim && !node && _vm.SelectedPart is not null;
    }

    // ── drag-drop ────────────────────────────────────────────────────────

    private async void OpenTemplates()
    {
        var store = App.Services.GetService<TemplateStore>();
        if (store is null || _vm is null) return;
        var dlg = new TemplateDialog(_vm, store);
        await dlg.ShowDialog(this);
    }

    private async void OpenSettings()
    {
        var dlg = new SettingsDialog();
        await dlg.ShowDialog<bool>(this);
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (_vm is null) return;
        var items = e.DataTransfer?.TryGetFiles();
        if (items is null) return;
        foreach (var item in items)
        {
            if (item is not IStorageFile f) continue;
            try
            {
                await using var stream = await f.OpenReadAsync();
                _vm.ImportFile(stream, f.Name);
            }
            catch { /* errors surfaced via StatusText */ }
        }
        _vm.Refresh();
    }

    // ── keyboard shortcuts ────────────────────────────────────────────────

    private void OnFitClick(object? sender, RoutedEventArgs e) => Viewport.FitToView();

    private void OnToggleGrid(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ShowGrid = !_vm.ShowGrid;
        Viewport.SetShowGrid(_vm.ShowGrid);
        SetToggle(BtnGridToggle, _vm.ShowGrid);
    }

    private void OnToggleSnap(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        bool on = !Viewport.SnapEnabled;
        Viewport.SetSnap(on);
        SetToggle(BtnSnapToggle, on);
        _vm.StatusText = on ? "Snap-to-grid on" : "Snap-to-grid off";
    }

    private void OnToggleCanvasDark(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.DarkCanvas = !_vm.DarkCanvas;
        Viewport.SetDarkCanvas(_vm.DarkCanvas);
        SetToggle(BtnCanvasDark, _vm.DarkCanvas);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (FocusManager?.GetFocusedElement() is TextBox) return;

        switch (e.Key)
        {
            case Key.Delete:
            case Key.Back:
                _vm.DeleteSelected();
                e.Handled = true;
                break;
            case Key.D when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.DuplicateSelected();
                e.Handled = true;
                break;
            case Key.F:
                Viewport.FitToView();
                e.Handled = true;
                break;
            case Key.Escape:
                if (_vm.InSimMode) _vm.ExitSimMode();
                else _vm.SelectedPart = null;
                e.Handled = true;
                break;
            case Key.Z when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.Undo();
                e.Handled = true;
                break;
            case Key.Y when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.Redo();
                e.Handled = true;
                break;
            case Key.C when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.CopySelected();
                e.Handled = true;
                break;
            case Key.V when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                _vm.PasteClipboard();
                e.Handled = true;
                break;
        }
    }
}
