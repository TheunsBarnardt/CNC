using Avalonia.Controls;
using Avalonia.Interactivity;
using Backend.Models;
using Desktop.ViewModels;

namespace Desktop.Views;

public partial class GuideDialog : Window
{
    private Guide?         _guide;
    private MainViewModel? _vm;
    private bool           _deleted;

    // Parameterless ctor required by Avalonia AXAML loader
    public GuideDialog() => InitializeComponent();

    public void Load(Guide guide, MainViewModel vm)
    {
        _guide = guide;
        _vm    = vm;

        TbId.Text    = $"Guideline ID: {guide.Id.ToString()[..8]}";
        TbDesc.Text  = DescribeGuide(guide);
        TbLabel.Text = guide.Label;
        TbX.Text     = guide.X.ToString("F5");
        TbY.Text     = guide.Y.ToString("F5");
        TbAngle.Text = guide.AngleDeg.ToString("F5");
        CbLocked.IsChecked = guide.IsLocked;

        BtnOk.Click        += OnOk;
        BtnDuplicate.Click += OnDuplicate;
        BtnDelete.Click    += OnDelete;
        BtnCancel.Click    += (_, _) => Close();
    }

    private void OnOk(object? s, RoutedEventArgs e) { Apply(); Close(true); }

    private void OnDuplicate(object? s, RoutedEventArgs e)
    {
        if (_vm is null || _guide is null) return;
        Apply();
        _vm.DuplicateGuide(_guide);
        Close(true);
    }

    private void OnDelete(object? s, RoutedEventArgs e)
    {
        if (_vm is null || _guide is null) return;
        _deleted = true;
        _vm.DeleteGuide(_guide);
        Close(false);
    }

    private void Apply()
    {
        if (_deleted || _guide is null || _vm is null) return;
        bool relative = CbRelative.IsChecked ?? false;

        if (double.TryParse(TbX.Text, out var x))
            _guide.X = relative ? _guide.X + x : x;
        if (double.TryParse(TbY.Text, out var y))
            _guide.Y = relative ? _guide.Y + y : y;
        if (double.TryParse(TbAngle.Text, out var a))
            _guide.AngleDeg = relative ? _guide.AngleDeg + a : a;

        _guide.Label    = TbLabel.Text?.Trim() ?? "";
        _guide.IsLocked = CbLocked.IsChecked ?? false;
        _vm.UpdateGuide(_guide);
    }

    private static string DescribeGuide(Guide g) =>
        Math.Abs(g.AngleDeg - 90) < 1 ? $"Current: vertical, at {g.X:F2} mm"
        : g.AngleDeg < 1               ? $"Current: horizontal, at {g.Y:F2} mm"
                                        : $"Current: {g.AngleDeg:F1}°, at ({g.X:F2}, {g.Y:F2}) mm";
}
