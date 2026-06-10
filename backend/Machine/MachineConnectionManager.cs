namespace Backend.Machine;

/// <summary>
/// Singleton that manages the active machine link and exposes all motion controls.
/// Starts with a <see cref="FakeMachineConnection"/> (status only, no hardware).
/// Call <see cref="ConnectSerialAsync"/> to swap in a real serial link at runtime.
///
/// All motion commands require a real serial connection; they return
/// <see cref="InvalidOperationException"/> when fake is active.
///
/// Job progress is tracked here and injected into every <see cref="MachineStatus"/>
/// emitted while a job is running — both via <see cref="StatusChanged"/> events and
/// via <see cref="GetStatus"/>. This keeps the neutral status record as the single
/// data shape flowing to SignalR clients.
/// </summary>
public sealed class MachineConnectionManager : IMachineConnection
{
    private readonly FakeMachineConnection _fake;
    private readonly object _gate = new();
    private IMachineConnection _inner;
    private SerialMachineConnection? _serial;

    // Active job state.
    private CancellationTokenSource? _jobCts;
    private Task? _jobTask;
    private int? _jobTotal;
    private int? _jobDone;

    public event EventHandler<MachineStatus>? StatusChanged;

    public MachineConnectionManager(FakeMachineConnection fake)
    {
        _fake = fake;
        _inner = fake;
        fake.StatusChanged += OnInnerStatus;
    }

    // ── IMachineConnection ────────────────────────────────────────────────

    public MachineConnectionState State { get { lock (_gate) return _inner.State; } }

    public MachineStatus GetStatus()
    {
        var s = _inner.GetStatus();
        lock (_gate)
            return (_jobTotal.HasValue) ? s with { JobTotal = _jobTotal, JobDone = _jobDone } : s;
    }

    public Task ConnectAsync(CancellationToken ct = default) => _inner.ConnectAsync(ct);
    public Task DisconnectAsync(CancellationToken ct = default) => _inner.DisconnectAsync(ct);

    // ── Serial management ─────────────────────────────────────────────────

    public bool IsSerialConnected
    {
        get { lock (_gate) return _serial is not null && _inner.State == MachineConnectionState.Connected; }
    }
    public string? ConnectedPort { get { lock (_gate) return _serial?.PortName; } }
    public int? ConnectedBaud { get { lock (_gate) return _serial?.BaudRate; } }

    public async Task ConnectSerialAsync(string port, int baud, CancellationToken ct = default)
    {
        SerialMachineConnection serial;
        lock (_gate)
        {
            _inner.StatusChanged -= OnInnerStatus;
            serial = new SerialMachineConnection(port, baud);
            _serial = serial;
            _inner = serial;
            serial.StatusChanged += OnInnerStatus;
        }
        try { await serial.ConnectAsync(ct); }
        catch
        {
            lock (_gate)
            {
                serial.StatusChanged -= OnInnerStatus;
                _serial = null;
                _inner = _fake;
                _fake.StatusChanged += OnInnerStatus;
            }
            await serial.DisposeAsync();
            throw;
        }
    }

    public async Task DisconnectSerialAsync(CancellationToken ct = default)
    {
        // Cancel any running job first.
        await StopJobAsync();

        SerialMachineConnection? serial;
        lock (_gate)
        {
            serial = _serial;
            if (serial is null) return;
            serial.StatusChanged -= OnInnerStatus;
            _serial = null;
            _inner = _fake;
            _fake.StatusChanged += OnInnerStatus;
        }
        await serial.DisconnectAsync(ct);
        await serial.DisposeAsync();
    }

    // ── Motion commands ───────────────────────────────────────────────────

    public async Task JogAsync(string axis, double distanceMm, double feedMmMin,
        CancellationToken ct = default)
    {
        var serial = RequireSerial();
        await serial.JogAsync(axis, distanceMm, feedMmMin, ct);
    }

    /// <summary>Starts homing in the background; returns immediately.</summary>
    public Task StartHomeAsync()
    {
        var serial = RequireSerial();
        return Task.Run(async () =>
        {
            try { await serial.HomeAsync(); }
            catch { /* errors surface via SignalR ALARM state */ }
        });
    }

    public async Task SetZeroAsync(CancellationToken ct = default)
    {
        var serial = RequireSerial();
        await serial.SetZeroAsync(ct);
    }

    public void FeedHold() => RequireSerial().FeedHold();
    public void Resume() => RequireSerial().Resume();
    public void SoftReset() => RequireSerial().SoftReset();

    // ── Job streaming ─────────────────────────────────────────────────────

    public bool IsJobRunning { get { lock (_gate) return _jobTask is not null; } }

    /// <summary>
    /// Start streaming <paramref name="lines"/> in the background.
    /// Returns false if a job is already running or no serial connection is active.
    /// </summary>
    public bool StartJob(IReadOnlyList<string> lines)
    {
        SerialMachineConnection? serial;
        lock (_gate)
        {
            serial = _serial;
            if (serial is null || _jobTask is not null) return false;

            _jobCts = new CancellationTokenSource();
            _jobTotal = lines.Count(l => l.TrimEnd().Length > 0);
            _jobDone = 0;
        }

        var cts = _jobCts!;
        _jobTask = Task.Run(async () =>
        {
            try
            {
                await serial.RunGcodeAsync(lines, (done, total) =>
                {
                    lock (_gate) { _jobDone = done; _jobTotal = total; }
                    // Push enriched status immediately so frontend tracks progress.
                    var s = serial.GetStatus() with { JobDone = done, JobTotal = total };
                    StatusChanged?.Invoke(this, s);
                }, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Surface streaming error as a status event so the UI sees it.
                var s = serial.GetStatus() with { MachineState = $"Error: {ex.Message.Split('\n')[0]}" };
                StatusChanged?.Invoke(this, s);
            }
            finally
            {
                lock (_gate) { _jobTotal = null; _jobDone = null; _jobTask = null; _jobCts = null; }
                cts.Dispose();
                // Push a clean idle status.
                StatusChanged?.Invoke(this, serial.GetStatus());
            }
        });

        return true;
    }

    public async Task StopJobAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_gate) { cts = _jobCts; task = _jobTask; }

        if (cts is not null) await cts.CancelAsync();
        if (task is not null)
            try { await task; } catch (OperationCanceledException) { }
    }

    // ── Private ───────────────────────────────────────────────────────────

    private SerialMachineConnection RequireSerial()
    {
        lock (_gate)
        {
            if (_serial is null || _inner.State != MachineConnectionState.Connected)
                throw new InvalidOperationException("Not connected to a real machine.");
            return _serial;
        }
    }

    private void OnInnerStatus(object? sender, MachineStatus s)
    {
        int? total, done;
        lock (_gate) { total = _jobTotal; done = _jobDone; }
        var enriched = total.HasValue ? s with { JobTotal = total, JobDone = done } : s;
        StatusChanged?.Invoke(this, enriched);
    }
}
