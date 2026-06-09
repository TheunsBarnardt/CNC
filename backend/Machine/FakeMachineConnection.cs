namespace Backend.Machine;

/// <summary>
/// A hardware-free <see cref="IMachineConnection"/> used until real serial
/// support lands (Milestone 3). It reports as "Connected" and emits a gently
/// moving fake position so the UI can show a visibly live status stream.
///
/// It performs no real I/O and, deliberately, no real motion — it only
/// fabricates status snapshots.
/// </summary>
public sealed class FakeMachineConnection : IMachineConnection
{
    private readonly object _gate = new();
    private long _sequence;
    private MachineConnectionState _state = MachineConnectionState.Connected;

    public MachineConnectionState State
    {
        get { lock (_gate) { return _state; } }
    }

    public event EventHandler<MachineStatus>? StatusChanged;

    public MachineStatus GetStatus()
    {
        long seq;
        MachineConnectionState state;
        lock (_gate)
        {
            seq = ++_sequence;
            state = _state;
        }

        // Trace a slow Lissajous-ish path so the position visibly changes each
        // tick. Purely cosmetic fake data — no relation to any real machine.
        double t = seq * 0.25;
        var status = new MachineStatus(
            ConnectionState: state,
            MachineState: "Idle",
            X: Math.Round(150 + 100 * Math.Sin(t), 2),
            Y: Math.Round(150 + 100 * Math.Sin(t * 0.5), 2),
            Z: 0,
            Timestamp: DateTimeOffset.UtcNow,
            HeartbeatSequence: seq);

        StatusChanged?.Invoke(this, status);
        return status;
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) { _state = MachineConnectionState.Connected; }
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) { _state = MachineConnectionState.Disconnected; }
        return Task.CompletedTask;
    }
}
