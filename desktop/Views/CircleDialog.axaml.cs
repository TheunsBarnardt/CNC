using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Desktop.Views;

public partial class CircleDialog : Window
{
    public double Radius_mm { get; private set; }

    public CircleDialog()
    {
        InitializeComponent();
        BtnCancel.Click += (_, _) => Close();
        BtnCreate.Click += OnCreate;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        if (double.TryParse(TbRadius.Text, out var r) && r > 0)
        {
            Radius_mm = r;
            Close(true);
        }
    }
}
