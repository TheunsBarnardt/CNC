namespace Backend.Machine;

/// <summary>
/// Singleton that manages the active machine link. Starts with a
/// <see cref="FakeMachineConnection"/> so the app runs without hardware.
/// Call <see cref="ConnectSerialAsync"/> / <see cref="DisconnectSerialAsync"/>
/// (from the machine REST API) to swap in a real GRBL serial connection at runtime.
///
/// Implements <see cref="IMachineConnection"/> so the heartbeat broadcaster and
/// SignalR hub don't need to know about the swap.
/// </summary>
public sealed class MachineConnectionManager : IMachineConnection
{
    private readonly FakeMachineConnection _fake;
    private readonly object _gate = new();
    private IMachineConnection _inner;
    private SerialMachineConnection? _serial;

    public event EventHandler<MachineStatus>? StatusChanged;

    public MachineConnectionManager(FakeMachineConnection fake)
    {
        _fake = fake;
        _inner = fake;
        fake.StatusChanged += OnInnerStatus;
    }

    // ── IMachineConnection ────────────────────────────────────────────────

    public MachineConnectionState State
    {
        get { lock (_gate) return _inner.State; }
    }

    public MachineStatus GetStatus()
    {
        lock (_gate) return _inner.GetStatus();
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        lock (_gate) return _inner.ConnectAsync(ct);
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        lock (_gate) return _inner.DisconnectAsync(ct);
    }

    // ── Serial management (called by MachineApi) ─────────────────────────

    /// <summary>Whether a real serial connection is currently active.</summary>
    public bool IsSerialConnected
    {
        get { lock (_gate) return _serial is not null && _inner.State == MachineConnectionState.Connected; }
    }

    /// <summary>Port name of the active serial connection, or null when on fake.</summary>
    public string? ConnectedPort
    {
        get { lock (_gate) return _serial?.PortName; }
    }

    public int? ConnectedBaud
    {
        get { lock (_gate) return _serial?.BaudRate; }
    }

    /// <summary>Swap the fake connection for a real serial link.</summary>
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
        // ConnectAsync may throw — if it does, revert to fake so the app stays usable.
        try
        {
            await serial.ConnectAsync(ct);
        }
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

    /// <summary>Disconnect the serial link and revert to the fake connection.</summary>
    public async Task DisconnectSerialAsync(CancellationToken ct = default)
    {
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

    // ── Private ───────────────────────────────────────────────────────────

    private void OnInnerStatus(object? sender, MachineStatus s) =>
        StatusChanged?.Invoke(this, s);
}
