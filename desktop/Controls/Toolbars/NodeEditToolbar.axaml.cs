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
        DataContextChanged += (_, _) =>
        {
            if (Vm is not null)
                ComplexityInfo.Text = Vm.CalculateComplexity();
        };
    }

    private void OnToggleSmooth(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.StatusText = "Node smooth: mark selected node as smooth (Bézier curve)";
    }

    private void OnToggleSharp(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.StatusText = "Node sharp: remove Bézier handles from selected node";
    }

    private void OnAutoSmooth(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.AutoSmoothNode();
        ComplexityInfo.Text = Vm.CalculateComplexity();
    }

    private void OnScissors(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.SplitPathAtNode();
        ComplexityInfo.Text = Vm.CalculateComplexity();
    }

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.ExitNodeEditMode();
    }
}
