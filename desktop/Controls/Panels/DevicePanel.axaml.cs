using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Backend.Cam;
using Backend.Machine;
using Desktop.ViewModels;
using Desktop.Views;

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

    private void OnHome       (object? s, RoutedEventArgs e) => _ = M?.HomeAsync();
    private void OnSetZero    (object? s, RoutedEventArgs e) => _ = M?.SetZeroAsync();
    private void OnSetWcsZero (object? s, RoutedEventArgs e) => _ = M?.SetWcsZeroAsync();
    private void OnUnlock     (object? s, RoutedEventArgs e) => _ = M?.UnlockAsync();

    private void OnWcsChanged(object? s, SelectionChangedEventArgs e)
    {
        if (M is null || CbWcs.SelectedItem is not ComboBoxItem ci
            || !int.TryParse(ci.Tag as string, out int idx)) return;
        M.WcsIndex = idx;
    }
    private void OnFeedHold(object? s, RoutedEventArgs e) => M?.FeedHold();
    private void OnResume  (object? s, RoutedEventArgs e) => M?.Resume();
    private void OnEStop   (object? s, RoutedEventArgs e) => M?.EStop();
    private void OnStop    (object? s, RoutedEventArgs e) => _ = M?.StopJobAsync();

    private async void OnRun(object? s, RoutedEventArgs e)
    {
        if (Vm is null || M is null) return;
        try
        {
            // Pre-flight check — show checklist and block run on failures.
            var owner = this.FindAncestorOfType<Window>();
            if (owner is not null)
            {
                var proj = Vm.Project;
                var camSettings = proj.Cam;
                var items = PreflightChecker.Check(proj, camSettings);
                var dlg = new PreflightDialog();
                dlg.Load(items);
                bool proceed = await dlg.ShowDialog<bool>(owner);
                if (!proceed) return;
            }

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

    // ── GRBL config ────────────────────────────────────────────────────────

    private async void OnReadSettings(object? s, RoutedEventArgs e)
    {
        if (M is null) return;
        BtnReadSettings.IsEnabled = false;
        GrblSettingsStatus.Text = "Reading…";
        try
        {
            await M.ReadGrblSettingsAsync();
            GrblSettingsStatus.Text = $"{M.GrblSettings.Count} settings loaded";
        }
        catch (Exception ex) { GrblSettingsStatus.Text = $"Error: {ex.Message}"; }
        finally { BtnReadSettings.IsEnabled = true; }
    }

    private async void OnWriteSetting(object? s, RoutedEventArgs e)
    {
        if (M is null || s is not Button btn || btn.Tag is not GrblSetting setting) return;
        try
        {
            await M.WriteGrblSettingAsync(setting.Id, setting.Value);
            GrblSettingsStatus.Text = $"${setting.Id}={setting.Value} written";
        }
        catch (Exception ex) { GrblSettingsStatus.Text = $"Error: {ex.Message}"; }
    }

    private void OnSettingValueCommit(object? s, RoutedEventArgs e)
    {
        // Value is TwoWay bound — just update the hint text.
        if (s is TextBox tb && tb.Tag is GrblSetting setting)
            GrblSettingsStatus.Text = $"${setting.Id} — press Set to write to machine";
    }

    private async void OnExportSettings(object? s, RoutedEventArgs e)
    {
        if (M is null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export GRBL settings",
            SuggestedFileName = "grbl_settings.json",
            FileTypeChoices = [new("JSON") { Patterns = ["*.json"] }],
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new System.IO.StreamWriter(stream);
        await writer.WriteAsync(M.ExportSettingsJson());
        GrblSettingsStatus.Text = "Settings exported";
    }

    private async void OnImportSettings(object? s, RoutedEventArgs e)
    {
        if (M is null) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import GRBL settings",
            AllowMultiple = false,
            FileTypeFilter = [new("JSON") { Patterns = ["*.json"] }],
        });
        if (files.Count == 0) return;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new System.IO.StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        GrblSettingsStatus.Text = "Writing settings to machine…";
        BtnImportSettings.IsEnabled = false;
        try
        {
            await M.ImportSettingsJsonAsync(json);
            GrblSettingsStatus.Text = "Settings imported and written to machine";
        }
        catch (Exception ex) { GrblSettingsStatus.Text = $"Error: {ex.Message}"; }
        finally { BtnImportSettings.IsEnabled = true; }
    }
}
