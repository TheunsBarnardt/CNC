using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Backend.Cam;
using Backend.Simulation;
using Desktop.ViewModels;

namespace Desktop.Controls.Toolbars;

public partial class SimulationBar : UserControl
{
    private MainViewModel? Vm => DataContext as MainViewModel;

    private Simulation?       _sim;
    private DispatcherTimer?  _timer;
    private bool              _playing;
    private double            _simTimeSec;
    private bool              _scrubbing;

    public SimulationBar()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => OnVmChanged();
    }

    private void OnVmChanged()
    {
        if (Vm is null) return;
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.InSimMode))
            {
                if (Vm.InSimMode) BuildSim();
                else StopPlayback();
            }
        };
    }

    private void BuildSim()
    {
        StopPlayback();
        _sim = null;
        _simTimeSec = 0;
        Scrub.Value = 0;
        try
        {
            var proj = Vm!.Project;
            var tp   = Backend.Cam.CamEngine.Generate(proj, proj.Cam);
            _sim     = SimulationBuilder.Build(tp, proj);
            TbStats.Text = $"{_sim.TotalTimeSec:F1}s · {_sim.TotalCutMm:F0}mm";
            BtnPlay.Content = "▶";
        }
        catch (Exception ex)
        {
            TbStats.Text = $"Sim failed: {ex.Message}";
        }
    }

    private void OnPlayPause(object? s, RoutedEventArgs e)
    {
        if (_sim is null) return;
        if (_playing) Pause(); else Play();
    }

    private void Play()
    {
        if (_sim is null) return;
        _playing = true;
        BtnPlay.Content = "⏸";
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void Pause()
    {
        _playing = false;
        BtnPlay.Content = "▶";
        _timer?.Stop();
        _timer = null;
    }

    private void StopPlayback()
    {
        Pause();
        _simTimeSec = 0;
        _sim = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_sim is null) { Pause(); return; }
        double speed = SpeedMultiplier();
        _simTimeSec += 0.016 * speed;
        if (_simTimeSec >= _sim.TotalTimeSec)
        {
            _simTimeSec = _sim.TotalTimeSec;
            Pause();
        }
        _scrubbing = true;
        Scrub.Value = _simTimeSec / _sim.TotalTimeSec;
        _scrubbing = false;
        RaiseSimTimeChanged();
    }

    private void OnScrub(object? s, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_scrubbing || _sim is null) return;
        _simTimeSec = Scrub.Value * _sim.TotalTimeSec;
        RaiseSimTimeChanged();
    }

    private void OnStop(object? s, RoutedEventArgs e)
    {
        Pause();
        _simTimeSec = 0;
        Scrub.Value = 0;
        RaiseSimTimeChanged();
    }

    private void OnBack(object? s, RoutedEventArgs e) => Vm?.ExitSimMode();

    private double SpeedMultiplier()
    {
        if (CbSpeed.SelectedItem is ComboBoxItem item && double.TryParse(item.Tag as string, out var v)) return v;
        return 1;
    }

    private void RaiseSimTimeChanged()
    {
        if (_sim is null) return;
        var state = SimulationRunner.StateAt(_sim, _simTimeSec);
        SimTimeChanged?.Invoke(this, state);
    }

    /// <summary>Fires on every tick or scrub so ViewportControl can redraw the torch position.</summary>
    public event EventHandler<SimState>? SimTimeChanged;
}
