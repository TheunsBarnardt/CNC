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
