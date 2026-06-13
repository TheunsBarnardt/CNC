using Avalonia.Controls;
using Avalonia.Interactivity;
using Backend.Models;
using Desktop.ViewModels;
using Desktop.Views;

namespace Desktop.Controls.Panels;

public partial class LayersPanel : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public LayersPanel() => InitializeComponent();

    private void OnAddLayer(object? s, RoutedEventArgs e) => Vm?.AddLayer();

    private async void OnChangeColor(object? s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not Layer layer) return;

        var dlg = new ColorPickerDialog { SelectedColor = layer.Color };
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        if (await dlg.ShowDialog<bool>(owner))
        {
            Vm?.UpdateLayer(layer, color: dlg.SelectedColor);
        }
    }

    private void OnOperationModeChanged(object? s, SelectionChangedEventArgs e)
    {
        if (s is ComboBox cb && cb.Tag is Layer layer && cb.SelectedItem is string modeStr)
        {
            if (System.Enum.TryParse<LayerOperationMode>(modeStr, out var mode))
                Vm?.UpdateLayer(layer, operationMode: mode);
        }
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
}
