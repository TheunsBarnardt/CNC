using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

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
    public string Param1Tag { get; private set; } = "Power";
    public double Param1From { get; private set; }
    public double Param1To { get; private set; }
    public string Param2Tag { get; private set; } = "FeedRate";
    public double Param2From { get; private set; }
    public double Param2To { get; private set; }
    public int TestRows { get; private set; }
    public int TestCols { get; private set; }

    // Set by the caller so OnDownloadTest can use it
    public Func<ArrayPanel, Task>? TestDownloadHandler { get; set; }

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
            // Test tab has its own Download button — Create does nothing for it
        }
        catch { }
    }

    private async void OnDownloadTest(object? s, RoutedEventArgs e)
    {
        if (!CollectTestParams()) return;
        if (TestDownloadHandler is { } handler)
            await handler(this);
    }

    private bool CollectTestParams()
    {
        if (!int.TryParse(TbTestRows.Text, out var rows) || rows < 1) return false;
        if (!int.TryParse(TbTestCols.Text, out var cols) || cols < 1) return false;
        if (!double.TryParse(TbParam1From.Text, out var p1from)) return false;
        if (!double.TryParse(TbParam1To.Text, out var p1to)) return false;
        if (!double.TryParse(TbParam2From.Text, out var p2from)) return false;
        if (!double.TryParse(TbParam2To.Text, out var p2to)) return false;

        TestRows = rows;
        TestCols = cols;
        Param1Tag = (CbParam1.SelectedItem as ComboBoxItem)?.Tag as string ?? "Power";
        Param1From = p1from;
        Param1To = p1to;
        Param2Tag = (CbParam2.SelectedItem as ComboBoxItem)?.Tag as string ?? "FeedRate";
        Param2From = p2from;
        Param2To = p2to;
        return true;
    }
}
