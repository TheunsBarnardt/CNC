using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Desktop.ViewModels;

namespace Desktop.Controls.Panels;

public partial class GcodePanel : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;
    private record PostEntry(string Id, string Name);

    private bool _editMode;
    private string? _generatedText; // last generated text (for Regenerate)

    // Syntax colours
    private static readonly IBrush ColRapid   = new SolidColorBrush(Color.Parse("#888888"));
    private static readonly IBrush ColCut     = new SolidColorBrush(Color.Parse("#4ade80"));
    private static readonly IBrush ColComment = new SolidColorBrush(Color.Parse("#555577"));
    private static readonly IBrush ColTorch   = new SolidColorBrush(Color.Parse("#fb923c"));
    private static readonly IBrush ColPause   = new SolidColorBrush(Color.Parse("#facc15"));
    private static readonly IBrush ColDefault = new SolidColorBrush(Color.Parse("#e2e8f0"));

    public GcodePanel()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            LoadPosts();
            if (Vm is not null)
                Vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(MainViewModel.GcodeText) && !_editMode)
                        RenderSyntax(Vm.GcodeText);
                };
        };
    }

    private void LoadPosts()
    {
        if (Vm is null) return;
        var posts = Vm.GetPostProcessors()
                      .Select(p => new PostEntry(p.Id, p.Name + (p.IsDefault ? " (default)" : "")))
                      .ToList();
        CbPost.ItemsSource              = posts;
        CbPost.SelectedIndex            = 0;
        CbPost.DisplayMemberBinding     = new Avalonia.Data.Binding("Name");
    }

    private void OnGenerate(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var postId = (CbPost.SelectedItem as PostEntry)?.Id;
        try
        {
            Vm.GenerateGcodeString(postId);
            _generatedText = Vm.GcodeText;
            if (!_editMode) RenderSyntax(_generatedText);
        }
        catch (Exception ex) { Vm.StatusText = ex.Message; }
    }

    private void OnSaveFile(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var win = TopLevel.GetTopLevel(this);
        if (win is null) return;
        _ = Vm.GenerateGcodeAsync(win.StorageProvider);
    }

    // ── Edit mode ────────────────────────────────────────────────────────

    private void OnEditToggle(object? s, RoutedEventArgs e)
    {
        _editMode = !_editMode;
        BtnEditToggle.Content = _editMode ? "View" : "Edit";
        EditActions.IsVisible = _editMode;
        SvHighlight.IsVisible = !_editMode;
        SvEdit.IsVisible       = _editMode;

        if (_editMode)
        {
            TbEdit.Text = Vm?.GcodeText ?? "";
        }
        else
        {
            RenderSyntax(Vm?.GcodeText ?? "");
        }
    }

    private void OnRegenerate(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        var postId = (CbPost.SelectedItem as PostEntry)?.Id;
        try
        {
            Vm.GenerateGcodeString(postId);
            _generatedText = Vm.GcodeText;
            TbEdit.Text = _generatedText;
        }
        catch (Exception ex) { Vm.StatusText = ex.Message; }
    }

    private void OnApplyEdits(object? s, RoutedEventArgs e)
    {
        if (Vm is null) return;
        Vm.GcodeText = TbEdit.Text ?? "";
        EditHint.Text = "Edits applied — used on next Run";
    }

    // ── Syntax highlighting ───────────────────────────────────────────────

    private void RenderSyntax(string? text)
    {
        SyntaxLines.Items.Clear();
        if (string.IsNullOrEmpty(text)) return;

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var color = ClassifyLine(line);

            SyntaxLines.Items.Add(new TextBlock
            {
                Text       = line,
                Foreground = color,
                FontFamily = new FontFamily("Courier New, Consolas, monospace"),
                FontSize   = 11,
                Margin     = new Thickness(0, 0.5, 0, 0.5),
            });
        }
    }

    private static IBrush ClassifyLine(string line)
    {
        var t = line.TrimStart();
        if (t.StartsWith(';'))                              return ColComment;
        if (t.StartsWith("M3", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("M5", StringComparison.OrdinalIgnoreCase))   return ColTorch;
        if (t.StartsWith("G4 ", StringComparison.OrdinalIgnoreCase))  return ColPause;
        if (t.StartsWith("G0",  StringComparison.OrdinalIgnoreCase))  return ColRapid;
        if (t.StartsWith("G1",  StringComparison.OrdinalIgnoreCase))  return ColCut;
        return ColDefault;
    }
}
