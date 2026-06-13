using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Desktop.Views;

public partial class StarDialog : Window
{
    public int PointCount { get; private set; }
    public double OuterRadius_mm { get; private set; }
    public double InnerRadius_mm { get; private set; }

    public StarDialog()
    {
        InitializeComponent();
        BtnCancel.Click += (_, _) => Close();
        BtnCreate.Click += OnCreate;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (int.TryParse(TbPoints.Text, out var points) && points >= 3 &&
            double.TryParse(TbOuterRadius.Text, out var outer) && outer > 0 &&
            double.TryParse(TbInnerRadius.Text, out var inner) && inner > 0 && inner < outer)
        {
            PointCount = points;
            OuterRadius_mm = outer;
            InnerRadius_mm = inner;
            Close(true);
        }
    }
}
