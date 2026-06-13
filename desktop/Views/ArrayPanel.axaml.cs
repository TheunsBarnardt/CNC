using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Desktop.Views;

public partial class ArrayPanel : Window
{
    public enum ArrayType { Grid, Circular, Test }
    public ArrayType Type { get; private set; }

    // Grid
    public int GridRows { get; private set; }
    public int GridCols { get; private set; }
    public double GridSpacingMm { get; private set; }
    public bool AutoStep { get; private set; }

    // Circular
    public int CircularCount { get; private set; }
    public double CircularRadiusMm { get; private set; }
    public double StartAngleDeg { get; private set; }
    public bool RotateWithArray { get; private set; }

    // Test
    public string Param1 { get; private set; } = "";
    public string Param2 { get; private set; } = "";
    public int TestRows { get; private set; }
    public int TestCols { get; private set; }

    public ArrayPanel()
    {
        InitializeComponent();
        BtnCancel.Click += (_, _) => Close();
        BtnCreate.Click += OnCreate;
    }

    private void OnCreate(object? sender, RoutedEventArgs e)
    {
        var tabControl = this.FindControl<TabControl>("TabControl") ?? new TabControl();
        int selectedIdx = tabControl.SelectedIndex;

        try
        {
            if (selectedIdx == 0) // Grid
            {
                if (int.TryParse(TbGridRows.Text, out var rows) && rows > 0 &&
                    int.TryParse(TbGridCols.Text, out var cols) && cols > 0 &&
                    double.TryParse(TbGridSpacing.Text, out var spacing) && spacing > 0)
                {
                    Type = ArrayType.Grid;
                    GridRows = rows;
                    GridCols = cols;
                    GridSpacingMm = spacing;
                    AutoStep = CbAutoStep.IsChecked ?? false;
                    Close(true);
                }
            }
            else if (selectedIdx == 1) // Circular
            {
                if (int.TryParse(TbCircularCount.Text, out var count) && count >= 2 &&
                    double.TryParse(TbCircularRadius.Text, out var radius) && radius > 0 &&
                    double.TryParse(TbStartAngle.Text, out var angle))
                {
                    Type = ArrayType.Circular;
                    CircularCount = count;
                    CircularRadiusMm = radius;
                    StartAngleDeg = angle;
                    RotateWithArray = CbRotateWithArray.IsChecked ?? false;
                    Close(true);
                }
            }
            else if (selectedIdx == 2) // Test
            {
                if (int.TryParse(TbTestRows.Text, out var rows2) && rows2 > 0 &&
                    int.TryParse(TbTestCols.Text, out var cols2) && cols2 > 0)
                {
                    Type = ArrayType.Test;
                    Param1 = (CbParam1.SelectedItem as string) ?? "";
                    Param2 = (CbParam2.SelectedItem as string) ?? "";
                    TestRows = rows2;
                    TestCols = cols2;
                    Close(true);
                }
            }
        }
        catch
        {
            // Validation will catch this
        }
    }
}
