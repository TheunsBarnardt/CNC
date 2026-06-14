using Avalonia.Controls;
using Avalonia.Interactivity;
using Desktop.ViewModels;

namespace Desktop.Controls.Panels;

public partial class NestPanel : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public NestPanel() => InitializeComponent();

    private void OnNestClick(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;

        double margin  = double.TryParse(TbMargin.Text,  out var m) ? m : 5;
        double spacing = double.TryParse(TbSpacing.Text, out var sp) ? sp : 2;
        int rotStep = CbRotStep.SelectedItem is ComboBoxItem item && item.Tag is string t
            ? int.Parse(t) : 90;

        var (placed, skipped, warnings, utilPct) = Vm.Nest(margin, spacing, rotStep);
        TbNestResult.IsVisible = true;
        string utilStr = $"  ({utilPct:F1}% utilization)";
        TbNestResult.Text = skipped == 0
            ? $"Nested {placed} part(s).{utilStr}"
            : $"Nested {placed} part(s), skipped {skipped}.{utilStr}" +
              (warnings.Count > 0 ? " " + string.Join("; ", warnings) : "");
    }
}
