using System.Collections.ObjectModel;
using Avalonia.Threading;
using Backend.Machine;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.ViewModels;

/// <summary>
/// Observable wrapper around MachineConnectionManager.
/// Listens to StatusChanged events and updates UI-thread properties so
/// the DevicePanel AXAML bindings stay live without polling.
/// </summary>
public sealed class MachineViewModel : ObservableObject
{
    private readonly MachineConnectionManager _mgr;

    private bool _isConnected;
    public bool IsConnected { get => _isConnected; private set => SetProperty(ref _isConnected, value); }

    private string _machineState = "Offline";
    public string MachineState { get => _machineState; private set => SetProperty(ref _machineState, value); }

    private double _x, _y, _z;
    public double X { get => _x; private set => SetProperty(ref _x, value); }
    public double Y { get => _y; private set => SetProperty(ref _y, value); }
    public double Z { get => _z; private set => SetProperty(ref _z, value); }

    private int? _jobDone, _jobTotal;
    public int? JobDone  { get => _jobDone;  private set => SetProperty(ref _jobDone,  value); }
    public int? JobTotal { get => _jobTotal; private set => SetProperty(ref _jobTotal, value); }

    private bool _isJobRunning;
    public bool IsJobRunning { get => _isJobRunning; private set => SetProperty(ref _isJobRunning, value); }

    private string? _connectedPort;
    public string? ConnectedPort { get => _connectedPort; private set => SetProperty(ref _connectedPort, value); }

    private int? _connectedBaud;
    public int? ConnectedBaud { get => _connectedBaud; private set => SetProperty(ref _connectedBaud, value); }

    private string _errorText = "";
    public string ErrorText { get => _errorText; set => SetProperty(ref _errorText, value); }

    /// <summary>Last observed job progress percentage (0–100).</summary>
    private double _jobProgressPct;
    public double JobProgressPct { get => _jobProgressPct; private set => SetProperty(ref _jobProgressPct, value); }

    public ObservableCollection<JobLogEntry> JobLog { get; } = [];

    public MachineViewModel(MachineConnectionManager mgr)
    {
        _mgr = mgr;
        _mgr.StatusChanged += OnStatusChanged;
    }

    private void OnStatusChanged(object? sender, MachineStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected    = _mgr.IsSerialConnected;
            MachineState   = status.MachineState;
            X              = status.X;
            Y              = status.Y;
            Z              = status.Z;
            JobDone        = status.JobDone;
            JobTotal       = status.JobTotal;
            IsJobRunning   = _mgr.IsJobRunning;
            ConnectedPort  = _mgr.ConnectedPort;
            ConnectedBaud  = _mgr.ConnectedBaud;
            JobProgressPct = (status.JobTotal is > 0 && status.JobDone.HasValue)
                ? (double)status.JobDone.Value / status.JobTotal.Value * 100
                : 0;
        });
    }

    // ── Port discovery ────────────────────────────────────────────────────

    public Task<string[]> GetPortsAsync() =>
        Task.Run(() => System.IO.Ports.SerialPort.GetPortNames());

    // ── Connection ────────────────────────────────────────────────────────

    public async Task ConnectAsync(string port, int baud)
    {
        await _mgr.ConnectSerialAsync(port, baud);
        IsConnected   = true;
        ConnectedPort = _mgr.ConnectedPort;
        ConnectedBaud = _mgr.ConnectedBaud;
    }

    public async Task DisconnectAsync()
    {
        await _mgr.DisconnectSerialAsync();
        IsConnected   = false;
        ConnectedPort = null;
        ConnectedBaud = null;
    }

    // ── Motion ───────────────────────────────────────────────────────────

    public Task JogAsync(string axis, double dist, double feed) =>
        _mgr.JogAsync(axis, dist, feed);

    public Task HomeAsync()   => _mgr.StartHomeAsync();
    public Task SetZeroAsync() => _mgr.SetZeroAsync();
    public void FeedHold()    => _mgr.FeedHold();
    public void Resume()      => _mgr.Resume();
    public void EStop()       => _mgr.SoftReset();
    public Task StopJobAsync() => _mgr.StopJobAsync();
    public Task UnlockAsync()  => _mgr.UnlockAsync();

    // ── Job streaming ─────────────────────────────────────────────────────

    public bool StartJob(IReadOnlyList<string> lines) => _mgr.StartJob(lines);

    public void RefreshJobLog()
    {
        var entries = _mgr.GetJobLog();
        Dispatcher.UIThread.Post(() =>
        {
            JobLog.Clear();
            foreach (var e in entries) JobLog.Add(e);
        });
    }
}
