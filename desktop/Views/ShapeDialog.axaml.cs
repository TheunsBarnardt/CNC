using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Desktop.Views;

public partial class ShapeDialog : Window
{
    public double Width_mm { get; private set; }
    public double Height_mm { get; private set; }
    public double Radius_mm { get; private set; }

    public ShapeDialog()
    {
        InitializeComponent();
        BtnCancel.Click += (_, _) => Close();
        BtnCreate.Click += OnCreate;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (double.TryParse(TbWidth.Text, out var w) && w > 0 &&
            double.TryParse(TbHeight.Text, out var h) && h > 0 &&
            double.TryParse(TbRadius.Text, out var r) && r >= 0)
        {
            Width_mm = w;
            Height_mm = h;
            Radius_mm = r;
            Close(true);
        }
    }
}
