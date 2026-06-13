using Avalonia.Controls;
using Avalonia.Interactivity;
using Backend.Models;
using Desktop.ViewModels;

namespace Desktop.Controls.Panels;

public partial class LayersPanel : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public LayersPanel() => InitializeComponent();

    private void OnAddLayer(object? s, RoutedEventArgs e) => Vm?.AddLayer();

    private void OnChangeColor(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is Layer layer)
        {
            // TODO: Open color picker dialog
            Vm?.StatusText = $"Color picker for '{layer.Name}' not yet implemented";
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
