using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Backend.Cam;
using Backend.Models;

namespace Desktop.Views;

public partial class PreflightDialog : Window
{
    public PreflightDialog()
    {
        InitializeComponent();
        BtnCancel.Click += (_, _) => Close(false);
        BtnRun.Click += (_, _) => Close(true);
    }

    public void Load(List<PreflightItem> items)
    {
        int fails = items.Count(i => i.Status == PreflightStatus.Fail);
        int warns = items.Count(i => i.Status == PreflightStatus.Warn);
        int passes = items.Count(i => i.Status == PreflightStatus.Pass);

        BtnRun.IsEnabled = fails == 0;

        TbSummary.Text = fails > 0
            ? $"{fails} issue(s) must be resolved before cutting."
            : warns > 0
            ? $"{passes} passed, {warns} warning(s)."
            : "All checks passed.";

        // Render items manually (no compiled bindings on anonymous DataTemplate)
        CheckList.Items.Clear();
        foreach (var item in items)
        {
            var icon = new TextBlock
            {
                Text = item.Status switch
                {
                    PreflightStatus.Pass => "✓",
                    PreflightStatus.Warn => "⚠",
                    PreflightStatus.Fail => "✕",
                    _ => "?",
                },
                Foreground = item.Status switch
                {
                    PreflightStatus.Pass => new SolidColorBrush(Color.Parse("#22c55e")),
                    PreflightStatus.Warn => new SolidColorBrush(Color.Parse("#f59e0b")),
                    PreflightStatus.Fail => new SolidColorBrush(Color.Parse("#ef4444")),
                    _ => Brushes.Gray,
                },
                FontSize = 13,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Avalonia.Thickness(0, 2, 0, 0),
            };
            var nameBlock = new TextBlock
            {
                Text = item.Name,
                FontWeight = FontWeight.Medium,
                FontSize = 13,
            };
            var detailBlock = new TextBlock
            {
                Text = item.Detail,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#8899aa")),
                TextWrapping = TextWrapping.Wrap,
            };
            var row = new Border
            {
                BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
                BorderBrush = new SolidColorBrush(Color.Parse("#2a2a3a")),
                Padding = new Avalonia.Thickness(8, 6),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("20,*"),
                    Children =
                    {
                        icon,
                        new StackPanel
                        {
                            Spacing = 1,
                            Children = { nameBlock, detailBlock },
                            [Grid.ColumnProperty] = 1,
                        },
                    },
                },
            };
            CheckList.Items.Add(row);
        }
    }
}
