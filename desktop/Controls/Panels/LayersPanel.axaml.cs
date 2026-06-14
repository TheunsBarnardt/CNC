using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
        if (LayersList is null) return;
        LayersList.ItemsSource = vm.Layers;
        LayersEmptyHint.IsVisible = vm.Layers.Count == 0;
        vm.Layers.CollectionChanged += (_, _) =>
            LayersEmptyHint.IsVisible = _vm?.Layers.Count == 0;
    }

    private void OnDataContextSet(object? s, EventArgs e)
    {
        if (DataContext is MainViewModel vm && LayersList is not null)
            BindToVm(vm);
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            BindToVm(vm);
    }

    private void OnAddLayer(object? s, RoutedEventArgs e) => Vm?.AddLayer();

    private async void OnChangeColor(object? s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not Layer layer) return;
        var dlg = new ColorPickerDialog { SelectedColor = layer.Color };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;
        if (await dlg.ShowDialog<bool>(owner))
            Vm?.UpdateLayer(layer, color: dlg.SelectedColor);
    }

    // Layer name is TwoWay bound; commit on Enter or focus-lost so the model is up to date.
    private void OnNameCommit(object? s, RoutedEventArgs e)
    {
        if (s is TextBox tb && tb.Tag is Layer layer)
            Vm?.UpdateLayer(layer, name: tb.Text?.Trim() ?? layer.Name);
    }

    private void OnNameKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && s is TextBox tb && tb.Tag is Layer layer)
        {
            Vm?.UpdateLayer(layer, name: tb.Text?.Trim() ?? layer.Name);
            e.Handled = true;
        }
    }

    private void OnCycleOperationMode(object? s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not Layer layer) return;
        var modes = Enum.GetValues<LayerOperationMode>();
        int next = ((int)layer.OperationMode + 1) % modes.Length;
        Vm?.UpdateLayer(layer, operationMode: modes[next]);
    }

    private void OnToggleLock(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is Layer layer)
            Vm?.UpdateLayer(layer, locked: !layer.Locked);
    }

    private void OnToggleVisible(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is Layer layer)
            Vm?.UpdateLayer(layer, visible: !layer.Visible);
    }

    private void OnDeleteLayer(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is Layer layer)
            Vm?.DeleteLayer(layer);
    }

    /// <summary>Clicking a layer row makes it the active drawing layer and selects its parts.</summary>
    private void OnLayerRowPressed(object? s, PointerPressedEventArgs e)
    {
        if (Vm is null) return;
        if (s is Border border && border.Tag is Layer layer)
        {
            Vm.SetActiveLayer(layer.Id);
            Vm.SelectLayerParts(layer.Id);
        }
    }

    private void OnFeedOverrideCommit(object? s, RoutedEventArgs e)
    {
        if (s is not TextBox tb || tb.Tag is not Layer layer) return;
        var v = string.IsNullOrWhiteSpace(tb.Text) ? (double?)null
              : double.TryParse(tb.Text, out var d) ? d : layer.FeedRateMmMinOverride;
        Vm?.SetLayerFeedOverride(layer, v);
    }

    private void OnPowerOverrideCommit(object? s, RoutedEventArgs e)
    {
        if (s is not TextBox tb || tb.Tag is not Layer layer) return;
        var v = string.IsNullOrWhiteSpace(tb.Text) ? (double?)null
              : double.TryParse(tb.Text, out var d) ? Math.Clamp(d, 0, 100) : layer.LaserPowerPercentOverride;
        Vm?.SetLayerPowerOverride(layer, v);
    }

    private void OnOverrideKeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || s is not TextBox tb) return;
        TopLevel.GetTopLevel(tb)?.Focus();   // move focus away → triggers LostFocus commit
        e.Handled = true;
    }
}
