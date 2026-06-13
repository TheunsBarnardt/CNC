using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Desktop.Views;

public partial class ColorPickerDialog : Window
{
    public string SelectedColor { get; set; } = "#3b82f6";

    public ColorPickerDialog()
    {
        InitializeComponent();
        BtnCancel.Click += (_, _) => Close();
        BtnSelect.Click += OnSelect;
    }

    private void OnColorSelect(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string color)
        {
            SelectedColor = color;
            TbHex.Text = color;
        }
    }

    private void OnSelect(object? sender, RoutedEventArgs e)
    {
        string hex = TbHex.Text?.Trim() ?? "#3b82f6";

        // Validate hex color format
        if (!hex.StartsWith("#") || (hex.Length != 7 && hex.Length != 4))
        {
            hex = "#3b82f6"; // default fallback
        }

        SelectedColor = hex;
        Close(true);
    }
}
