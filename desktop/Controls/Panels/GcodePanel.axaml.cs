using Avalonia.Controls;
using Avalonia.Interactivity;
using Desktop.ViewModels;

namespace Desktop.Controls.Panels;

public partial class GcodePanel : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;
    private record PostEntry(string Id, string Name);

    public GcodePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => LoadPosts();
    }

    private void LoadPosts()
    {
        if (Vm is null) return;
        var posts = Vm.GetPostProcessors()
                      .Select(p => new PostEntry(p.Id, p.Name + (p.IsDefault ? " (default)" : "")))
                      .ToList();
        CbPost.ItemsSource     = posts;
        CbPost.SelectedIndex   = 0;
        CbPost.DisplayMemberBinding = new Avalonia.Data.Binding("Name");
    }

    private void OnGenerate(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var postId = (CbPost.SelectedItem as PostEntry)?.Id;
        try { Vm.GenerateGcodeString(postId); }
        catch (Exception ex) { Vm.StatusText = ex.Message; }
    }

    private void OnSaveFile(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var win = TopLevel.GetTopLevel(this);
        if (win is null) return;
        _ = Vm.GenerateGcodeAsync(win.StorageProvider);
    }
}
