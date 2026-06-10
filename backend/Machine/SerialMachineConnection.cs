using System.Globalization;
using System.IO.Ports;

namespace Backend.Machine;

/// <summary>
/// Real GRBL serial connection (System.IO.Ports). Opens the port, sends '?'
/// every 200 ms, parses the response status report, and fires StatusChanged.
/// No motion is performed — connection and status polling only.
///
/// SAFETY: DtrEnable = false prevents the Arduino auto-reset that would
/// momentarily energize the plasma relay on some boards.
/// </summary>
public sealed class SerialMachineConnection : IMachineConnection, IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public string PortName { get; }
    public int BaudRate { get; }

    private readonly object _gate = new();
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _pollLoop;
    private MachineConnectionState _state = MachineConnectionState.Disconnected;
    private MachineStatus _lastStatus;
    private long _seq;

    public event EventHandler<MachineStatus>? StatusChanged;

    public SerialMachineConnection(string portName, int baudRate = 115200)
    {
        PortName = portName;
        BaudRate = baudRate;
        _lastStatus = Snapshot(MachineConnectionState.Disconnected, "Idle", 0, 0, 0);
    }

    public MachineConnectionState State
    {
        get { lock (_gate) return _state; }
    }

    public MachineStatus GetStatus()
    {
        lock (_gate) return _lastStatus;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_state is MachineConnectionState.Connected or MachineConnectionState.Connecting)
                return;
            _state = MachineConnectionState.Connecting;
        }
        Publish(MachineConnectionState.Connecting, "Connecting", 0, 0, 0);

        try
        {
            var port = new SerialPort(PortName, BaudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = false,  // prevent Arduino auto-reset on open
                RtsEnable = false,
                ReadTimeout = 300,
                WriteTimeout = 500,
            };
            port.Open();
            lock (_gate)
            {
                _port = port;
                _state = MachineConnectionState.Connected;
            }
            Publish(MachineConnectionState.Connected, "Idle", 0, 0, 0);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _pollLoop = Task.Run(() => PollLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            lock (_gate) { _state = MachineConnectionState.Error; }
            Publish(MachineConnectionState.Error, $"Error", 0, 0, 0);
            throw new InvalidOperationException($"Could not open {PortName}: {ex.Message}", ex);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? loop;
        SerialPort? port;
        lock (_gate)
        {
            cts = _cts; _cts = null;
            loop = _pollLoop; _pollLoop = null;
            port = _port; _port = null;
            _state = MachineConnectionState.Disconnected;
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
        if (loop is not null)
        {
            try { await loop; } catch (OperationCanceledException) { }
        }
        if (port is not null)
        {
            try { port.Close(); } catch { /* best-effort */ }
            port.Dispose();
        }
        Publish(MachineConnectionState.Disconnected, "Idle", 0, 0, 0);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SerialPort? port;
            lock (_gate) { port = _port; }
            if (port is null) break;

            try
            {
                port.Write("?\n");
                // Read lines until we match a status report or time out.
                var deadline = Environment.TickCount64 + 400;
                while (Environment.TickCount64 < deadline && !ct.IsCancellationRequested)
                {
                    string line;
                    try
                    {
                        line = port.ReadLine().Trim('\r', '\n', ' ');
                    }
                    catch (TimeoutException)
                    {
                        break;
                    }
                    if (TryParseStatus(line, out var state, out var x, out var y, out var z))
                    {
                        Publish(MachineConnectionState.Connected, state, x, y, z);
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Port dropped (unplugged, etc.)
                lock (_gate) { _state = MachineConnectionState.Error; }
                Publish(MachineConnectionState.Error, "Error", 0, 0, 0);
                break;
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>
    /// Parses a GRBL status report line, e.g.:
    ///   <![CDATA[<Idle|MPos:0.000,0.000,0.000|WPos:0.000,0.000,0.000|FS:0,0>]]>
    /// Prefers WPos (work coordinates) over MPos.
    /// </summary>
    private static bool TryParseStatus(
        string line, out string state, out double x, out double y, out double z)
    {
        state = "Idle"; x = y = z = 0;
        if (line.Length < 3 || line[0] != '<') return false;

        // State field: everything between '<' and first '|', '>', or ':'
        var stateEnd = line.IndexOfAny(['|', '>', ':'], 1);
        state = stateEnd > 1 ? line[1..stateEnd] : "Unknown";

        // Prefer WPos, fall back to MPos
        return TryParsePos(line, "WPos:", out x, out y, out z)
            || TryParsePos(line, "MPos:", out x, out y, out z);
    }

    private static bool TryParsePos(
        string line, string key, out double x, out double y, out double z)
    {
        x = y = z = 0;
        var idx = line.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return false;
        var start = idx + key.Length;
        var end = line.IndexOfAny(['|', '>'], start);
        var segment = end < 0 ? line[start..] : line[start..end];
        var parts = segment.Split(',');
        if (parts.Length < 3) return false;
        return double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
    }

    private void Publish(MachineConnectionState connState, string machState,
        double x, double y, double z)
    {
        MachineStatus s;
        lock (_gate)
        {
            s = Snapshot(connState, machState, x, y, z);
            _lastStatus = s;
        }
        StatusChanged?.Invoke(this, s);
    }

    private MachineStatus Snapshot(MachineConnectionState connState, string machState,
        double x, double y, double z) =>
        new(connState, machState, x, y, z, DateTimeOffset.UtcNow, Interlocked.Increment(ref _seq));

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
