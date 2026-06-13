using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Desktop.Views;

public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object? s, RoutedEventArgs e) => Close();
}
