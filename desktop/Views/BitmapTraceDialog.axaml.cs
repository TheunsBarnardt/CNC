using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Desktop.Views;

public partial class BitmapTraceDialog : Window
{
    public enum TraceMode { Outline, Centerline }

    public TraceMode Mode { get; private set; }
    public int ThresholdValue { get; private set; } = 128;
    public bool InvertColors { get; private set; }
    public bool Grayscale { get; private set; }
    public bool AdjustBrightness { get; private set; }
    public bool AdjustContrast { get; private set; }
    public double SimplifyTolerance { get; private set; } = 0.1;

    public BitmapTraceDialog()
    {
        InitializeComponent();

        BtnCancel.Click += (_, _) => Close();
        BtnRetrace.Click += OnRetrace;
        BtnImport.Click += OnImport;
        SlrThreshold.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == nameof(Slider.Value))
            {
                ThresholdValue = (int)SlrThreshold.Value;
                TbPreviewInfo.Text = $"Threshold: {ThresholdValue}";
            }
        };
    }

    private void OnRetrace(object? sender, RoutedEventArgs e)
    {
        UpdateSettings();
        TbPreviewInfo.Text = $"Retrace with {(RbOutline.IsChecked == true ? "Outline" : "Centerline")} mode";
    }

    private void OnImport(object? sender, RoutedEventArgs e)
    {
        UpdateSettings();
        Close(true);
    }

    private void UpdateSettings()
    {
        Mode = RbOutline.IsChecked == true ? TraceMode.Outline : TraceMode.Centerline;
        ThresholdValue = (int)SlrThreshold.Value;
        InvertColors = CbInvert.IsChecked ?? false;
        Grayscale = CbGrayscale.IsChecked ?? false;
        AdjustBrightness = CbBrightness.IsChecked ?? false;
        AdjustContrast = CbContrast.IsChecked ?? false;

        if (double.TryParse(TbSimplify.Text, out var tolerance) && tolerance > 0)
            SimplifyTolerance = tolerance;
    }
}
