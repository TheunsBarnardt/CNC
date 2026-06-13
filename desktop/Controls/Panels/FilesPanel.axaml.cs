using Avalonia.Controls;
using Avalonia.Interactivity;
using Backend.Models;
using Desktop.ViewModels;
using Desktop.Views;

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

    private async void OnTraceBitmap(object? s, RoutedEventArgs e)
    {
        if (s is not Button btn || btn.Tag is not ImportedFile f) return;
        if (!f.IsBitmap) return;

        var dlg = new BitmapTraceDialog();
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner is null) return;

        if (await dlg.ShowDialog<bool>(owner))
        {
            Vm?.TraceBitmap(f, dlg);
        }
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
