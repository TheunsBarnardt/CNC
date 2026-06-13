using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Desktop.Views;

public partial class PolygonDialog : Window
{
    public int SideCount { get; private set; }
    public double Radius_mm { get; private set; }

    public PolygonDialog()
    {
        InitializeComponent();
        BtnCancel.Click += (_, _) => Close();
        BtnCreate.Click += OnCreate;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (int.TryParse(TbSides.Text, out var sides) && sides >= 3 &&
            double.TryParse(TbRadius.Text, out var r) && r > 0)
        {
            SideCount = sides;
            Radius_mm = r;
            Close(true);
        }
    }
}
