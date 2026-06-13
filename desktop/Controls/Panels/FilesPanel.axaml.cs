using Avalonia.Controls;
using Avalonia.Interactivity;
using Backend.Models;
using Desktop.ViewModels;

namespace Desktop.Controls.Panels;

public partial class FilesPanel : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    public FilesPanel() => InitializeComponent();

    private void OnImportClick(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var win = TopLevel.GetTopLevel(this);
        if (win is null) return;
        _ = Vm.ImportAsync(win.StorageProvider);
    }

    private void OnToggleVisible(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is ImportedFile f)
            Vm?.ToggleFileVisible(f);
    }

    private void OnAddToTable(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is ImportedFile f)
            Vm?.AddToTable(f);
    }

    private void OnRemoveFile(object? s, RoutedEventArgs e)
    {
        if (s is Button btn && btn.Tag is ImportedFile f)
            Vm?.RemoveFile(f);
    }
}
