using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Backend.Models;
using Desktop.ViewModels;
using Desktop.Views;

namespace Desktop.Controls.Panels;

public partial class FilesPanel : UserControl
{
    private MainViewModel? _vm;
    private MainViewModel? Vm => _vm;

    public FilesPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextSet;
        Loaded += OnLoaded;
    }

    private void BindToVm(MainViewModel vm)
    {
        _vm = vm;
        if (FilesList is null) return;
        FilesList.ItemsSource = vm.Files;
        FilesEmptyHint.IsVisible = vm.Files.Count == 0;
        vm.Files.CollectionChanged += (_, _) =>
            FilesEmptyHint.IsVisible = _vm?.Files.Count == 0;
    }

    private void OnDataContextSet(object? s, EventArgs e)
    {
        if (DataContext is MainViewModel vm && FilesList is not null)
            BindToVm(vm);
    }

    private void OnLoaded(object? s, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            BindToVm(vm);
    }

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

    // ── inline rename ─────────────────────────────────────────────────────
    private void OnFileNameDoubleTapped(object? s, TappedEventArgs e)
    {
        if (s is not TextBlock tb || tb.Tag is not ImportedFile) return;
        // Find the matching TextBox in the same template (same column 1)
        var parent = tb.Parent as Panel ?? tb.Parent as Control;
        if (parent is null) return;
        // Both FileNameLabel (the TextBlock) and FileNameEdit (the TextBox) live
        // inside the same Border; we toggle visibility and focus.
        tb.IsVisible = false;
        // Locate the editor by walking siblings.
        var editor = FindSibling<TextBox>(tb, "FileNameEdit");
        if (editor is not null)
        {
            editor.Text = ((ImportedFile)tb.Tag).Name;
            editor.IsVisible = true;
            editor.Focus();
            editor.SelectAll();
        }
        e.Handled = true;
    }

    private void OnFileNameEditKeyDown(object? s, KeyEventArgs e)
    {
        if (s is not TextBox tb) return;
        switch (e.Key)
        {
            case Key.Enter:
                CommitFileRename(tb);
                e.Handled = true;
                break;
            case Key.Escape:
                CancelFileRename(tb);
                e.Handled = true;
                break;
        }
    }

    private void OnFileNameEditLostFocus(object? s, RoutedEventArgs e)
    {
        if (s is TextBox tb && tb.IsVisible)
            CommitFileRename(tb);
    }

    private void CommitFileRename(TextBox tb)
    {
        if (tb.Tag is ImportedFile f)
        {
            var newName = tb.Text?.Trim();
            if (!string.IsNullOrEmpty(newName))
                f.Name = newName;
        }
        tb.IsVisible = false;
        var label = FindSibling<TextBlock>(tb, "FileNameLabel");
        if (label is not null) label.IsVisible = true;
        Vm?.Refresh();
    }

    private static void CancelFileRename(TextBox tb)
    {
        tb.IsVisible = false;
        var label = FindSibling<TextBlock>(tb, "FileNameLabel");
        if (label is not null) label.IsVisible = true;
    }

    /// <summary>Find a sibling control of the given name within the same parent panel.</summary>
    private static T? FindSibling<T>(Control anchor, string name) where T : Control
    {
        if (anchor.Parent is not Control parent) return null;
        return parent.FindControl<T>(name);
    }
}
