using Avalonia.Controls;
using Avalonia.Interactivity;
using Desktop.ViewModels;

namespace Desktop.Controls.Panels;

public partial class DevicePanel : UserControl
{
    private MainViewModel? Vm  => DataContext as MainViewModel;
    private MachineViewModel? M => Vm?.Machine;

    public DevicePanel() => InitializeComponent();

    // ── Connection ────────────────────────────────────────────────────────

    private void OnPortDropDown(object? s, EventArgs e)
    {
        _ = RefreshPortsAsync();
    }

    private async Task RefreshPortsAsync()
    {
        if (M is null) return;
        var ports = await M.GetPortsAsync();
        CbPort.ItemsSource   = ports;
        CbPort.SelectedIndex = 0;
    }

    private void OnConnectToggle(object? s, RoutedEventArgs e)
    {
        if (M is null) return;
        if (M.IsConnected) { _ = M.DisconnectAsync(); return; }
        var port = CbPort.SelectedItem as string;
        if (string.IsNullOrEmpty(port)) return;
        var baud = CbBaud.SelectedItem is ComboBoxItem bi && bi.Tag is string bt
            ? int.Parse(bt) : 115200;
        _ = M.ConnectAsync(port, baud);
    }

    // ── Jog ──────────────────────────────────────────────────────────────

    private void OnJog(object? s, RoutedEventArgs e)
    {
        if (M is null || s is not Button btn || btn.Tag is not string tag) return;
        double step = JogStep();
        double feed = JogFeed();
        var axis  = tag[0].ToString();
        double dir = tag[1] == '+' ? 1 : -1;
        _ = M.JogAsync(axis, step * dir, feed);
    }

    private double JogStep()
    {
        if (CbJogStep.SelectedItem is ComboBoxItem si && double.TryParse(si.Tag as string, out var v)) return v;
        return 1;
    }

    private double JogFeed()
    {
        if (CbJogFeed.SelectedItem is ComboBoxItem fi && double.TryParse(fi.Tag as string, out var v)) return v;
        return 1000;
    }

    // ── Motion commands ───────────────────────────────────────────────────

    private void OnHome    (object? s, RoutedEventArgs e) => _ = M?.HomeAsync();
    private void OnSetZero (object? s, RoutedEventArgs e) => _ = M?.SetZeroAsync();
    private void OnUnlock  (object? s, RoutedEventArgs e) => _ = M?.UnlockAsync();
    private void OnFeedHold(object? s, RoutedEventArgs e) => M?.FeedHold();
    private void OnResume  (object? s, RoutedEventArgs e) => M?.Resume();
    private void OnEStop   (object? s, RoutedEventArgs e) => M?.EStop();
    private void OnStop    (object? s, RoutedEventArgs e) => _ = M?.StopJobAsync();

    private void OnRun(object? s, RoutedEventArgs e)
    {
        if (Vm is null || M is null) return;
        try
        {
            var lines = Vm.GenerateGcodeString()
                          .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                          .Select(l => l.TrimEnd('\r'))
                          .ToList();
            if (!M.StartJob(lines))
                Vm.StatusText = "Cannot start job — check machine state";
        }
        catch (Exception ex) { Vm.StatusText = $"Generate failed: {ex.Message}"; }
    }

    private void OnRefreshLog(object? s, RoutedEventArgs e) => M?.RefreshJobLog();
}
