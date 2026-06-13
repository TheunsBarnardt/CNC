using Avalonia.Controls;
using Avalonia.Interactivity;
using Backend.Services;
using Desktop.ViewModels;

namespace Desktop.Views;

public partial class TemplateDialog : Window
{
    public TemplateDialog()
    {
        InitializeComponent();
    }

    public TemplateDialog(MainViewModel vm, TemplateStore store)
    {
        InitializeComponent();
    }

    private void OnClose(object? s, RoutedEventArgs e) => Close();
}
