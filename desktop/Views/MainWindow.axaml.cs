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

        // SimBar needs to hook into VM.InSimMode to rebuild the simulation
        SimBar.DataContext  = vm;
        EditBar.DataContext = vm;

        // All panels inherit DataContext from the window automatically,
        // but set explicitly to be safe
        PnlFiles.DataContext   = vm;
        PnlLayers.DataContext  = vm;
        PnlCut.DataContext     = vm;
        PnlNest.DataContext    = vm;
        PnlGcode.DataContext   = vm;
        PnlDevice.DataContext  = vm;

        // Canvas option checkboxes
        CbDarkCanvas.IsCheckedChanged += (_, _) =>
        {
            vm.DarkCanvas = CbDarkCanvas.IsChecked ?? true;
            Viewport.InvalidateVisual();
        };
        CbShowGrid.IsCheckedChanged += (_, _) =>
        {
            vm.ShowGrid = CbShowGrid.IsChecked ?? true;
            Viewport.InvalidateVisual();
        };

        // Header buttons
        BtnNew.Click      += (_, _)       => vm.NewProject();
        BtnOpen.Click     += async (_, _) => await vm.LoadAsync(StorageProvider);
        BtnSave.Click     += async (_, _) => await vm.SaveAsync(StorageProvider);
        BtnGcode.Click    += async (_, _) => await vm.GenerateGcodeAsync(StorageProvider);
        BtnSimMode.Click  += (_, _)       => vm.EnterSimMode();
        BtnFit.Click      += (_, _)       => Viewport.FitToView();
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
        TabFiles.Click  += (_, _) => ShowPanel(PnlFiles);
        TabLayers.Click += (_, _) => ShowPanel(PnlLayers);
        TabCut.Click    += (_, _) => ShowPanel(PnlCut);
        TabNest.Click   += (_, _) => ShowPanel(PnlNest);
        TabGcode.Click  += (_, _) => ShowPanel(PnlGcode);
        TabDevice.Click += (_, _) => ShowPanel(PnlDevice);

        // Simulation: sim time changes → viewport redraw
        SimBar.SimTimeChanged += (_, state) =>
        {
            Viewport.SimState = state;
            Viewport.InvalidateVisual();
        };

        // VM mode changes → show/hide sim vs edit toolbar
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(vm.InSimMode))
                Viewport.InvalidateVisual();
        };

        // Drag-drop onto viewport
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, (s, ea) => ea.DragEffects = DragDropEffects.Copy);
        DragDrop.SetAllowDrop(Viewport, true);

        // Keyboard shortcuts
        KeyDown += OnKeyDown;
    }

    // ── tab switching ─────────────────────────────────────────────────────

    private void ShowPanel(Control target)
    {
        PnlFiles.IsVisible  = target == PnlFiles;
        PnlLayers.IsVisible = target == PnlLayers;
        PnlCut.IsVisible    = target == PnlCut;
        PnlNest.IsVisible   = target == PnlNest;
        PnlGcode.IsVisible  = target == PnlGcode;
        PnlDevice.IsVisible = target == PnlDevice;
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

    private void OnToggleGrid(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.ShowGrid = !_vm.ShowGrid;
        BtnGridToggle.Background = _vm.ShowGrid ? null : new SolidColorBrush(Color.FromArgb(50, 100, 100, 100));
    }

    private void OnToggleSnap(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.StatusText = "Snap-to-grid: not fully implemented";
    }

    private void OnToggleCanvasDark(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        _vm.DarkCanvas = !_vm.DarkCanvas;
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
                // Undo — CheckpointService wired in future task
                e.Handled = true;
                break;
        }
    }
}
