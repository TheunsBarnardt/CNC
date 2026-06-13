using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Backend.Services;
using Desktop.Controls;
using Desktop.Controls.Toolbars;
using Desktop.ViewModels;
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
