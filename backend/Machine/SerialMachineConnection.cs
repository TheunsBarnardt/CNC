using System.Globalization;
using System.IO.Ports;
using System.Threading.Channels;

namespace Backend.Machine;

/// <summary>
/// Real GRBL serial connection. A dedicated reader loop dispatches all incoming
/// lines: status reports go to the status parser, ok/error responses go to the
/// response channel consumed by command senders and the G-code streamer.
///
/// SAFETY: DtrEnable = false prevents the Arduino auto-reset that would
/// momentarily energize the plasma relay on some boards. Motion commands
/// (Jog, Home, Run) are explicit-call-only — connecting never moves the machine.
/// </summary>
public sealed class SerialMachineConnection : IMachineConnection, IAsyncDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// GRBL RX buffer minus 1 byte reserved for '?' so we can always poll.
    /// </summary>
    private const int GrblBuffer = 127;

    public string PortName { get; }
    public int BaudRate { get; }

    private readonly object _gate = new();
    private SerialPort? _port;
    private CancellationTokenSource? _cts;
    private Task? _readLoop, _pollSender;
    private MachineConnectionState _state = MachineConnectionState.Disconnected;
    private MachineStatus _lastStatus;
    private long _seq;

    /// <summary>Serialises all writes so poll '?' and gcode lines never interleave.</summary>
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    /// <summary>ok / error responses produced by the reader, consumed by command senders.</summary>
    private readonly Channel<string> _responses =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = false });

    /// <summary>When non-null, the reader appends $N=value lines here (for $$ capture).</summary>
    private volatile List<string>? _settingsCapture;

    public event EventHandler<MachineStatus>? StatusChanged;

    public SerialMachineConnection(string portName, int baudRate = 115200)
    {
        PortName = portName;
        BaudRate = baudRate;
        _lastStatus = Snapshot(MachineConnectionState.Disconnected, "Idle", 0, 0, 0);
    }

    // ── IMachineConnection ────────────────────────────────────────────────

    public MachineConnectionState State { get { lock (_gate) return _state; } }
    public MachineStatus GetStatus() { lock (_gate) return _lastStatus; }

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
                DtrEnable = false,  // prevents Arduino auto-reset (plasma safety)
                RtsEnable = false,
                ReadTimeout = 500,
                WriteTimeout = 500,
            };
            port.Open();
            lock (_gate) { _port = port; _state = MachineConnectionState.Connected; }
            Publish(MachineConnectionState.Connected, "Idle", 0, 0, 0);

            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
            _pollSender = Task.Run(() => PollSenderAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            lock (_gate) { _state = MachineConnectionState.Error; }
            Publish(MachineConnectionState.Error, "Error", 0, 0, 0);
            throw new InvalidOperationException($"Could not open {PortName}: {ex.Message}", ex);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? cts;
        Task? rl, ps;
        SerialPort? port;
        lock (_gate)
        {
            cts = _cts; _cts = null;
            rl = _readLoop; _readLoop = null;
            ps = _pollSender; _pollSender = null;
            port = _port; _port = null;
            _state = MachineConnectionState.Disconnected;
        }
        if (cts is not null) { await cts.CancelAsync(); cts.Dispose(); }
        if (rl is not null) try { await rl; } catch (OperationCanceledException) { }
        if (ps is not null) try { await ps; } catch (OperationCanceledException) { }
        if (port is not null) { try { port.Close(); } catch { } port.Dispose(); }
        Publish(MachineConnectionState.Disconnected, "Idle", 0, 0, 0);
    }

    // ── Motion commands ───────────────────────────────────────────────────

    /// <summary>Jog one axis. Returns when GRBL acknowledges the command.</summary>
    public async Task JogAsync(string axis, double distanceMm, double feedMmMin,
        CancellationToken ct = default)
    {
        // GRBL jog: $J=G91 G21 X{dist} F{feed}
        var cmd = $"$J=G91 G21 {axis.ToUpperInvariant()}{Num(distanceMm)} F{Num(feedMmMin)}";
        await SendAndWaitAsync(cmd, ct);
    }

    /// <summary>Run the homing cycle ($H). Returns when homing is complete.</summary>
    public async Task HomeAsync(CancellationToken ct = default) =>
        await SendAndWaitAsync("$H", ct);

    /// <summary>Set work coordinate origin at current machine position (G10 L20 P1).</summary>
    public async Task SetZeroAsync(CancellationToken ct = default) =>
        await SendAndWaitAsync("G10 L20 P1 X0 Y0 Z0", ct);

    /// <summary>Real-time feed hold '!' — no ok response.</summary>
    public void FeedHold() => SendRealtime("!");

    /// <summary>Real-time resume '~' — no ok response.</summary>
    public void Resume() => SendRealtime("~");

    /// <summary>Real-time soft reset Ctrl-X — clears buffer, enters Alarm.</summary>
    public void SoftReset() => SendRealtime("\x18");

    /// <summary>Send $X to clear an Alarm lock after E-Stop or homing failure.</summary>
    public async Task UnlockAsync(CancellationToken ct = default) =>
        await SendAndWaitAsync("$X", ct);

    // ── GRBL config ($$ / $N=value) ───────────────────────────────────────

    /// <summary>
    /// Send $$ and collect all $N=value lines until the trailing "ok".
    /// </summary>
    public async Task<IReadOnlyList<GrblSetting>> ReadSettingsAsync(CancellationToken ct = default)
    {
        var capture = new List<string>();

        await _writeLock.WaitAsync(ct);
        try
        {
            while (_responses.Reader.TryRead(out _)) { }
            _settingsCapture = capture;   // arm capture before sending
            GetPort().Write("$$\n");
        }
        finally { _writeLock.Release(); }

        // Wait for 'ok' — the read loop clears _settingsCapture before writing it.
        var resp = await _responses.Reader.ReadAsync(ct);
        if (resp.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"GRBL: {resp}");

        // Parse $N=value lines collected by the read loop.
        var settings = new List<GrblSetting>();
        foreach (var line in capture)
        {
            var eq = line.IndexOf('=');
            if (eq < 2) continue;
            if (!int.TryParse(line[1..eq], out int id)) continue;
            var val = line[(eq + 1)..].Trim();
            settings.Add(new GrblSetting { Id = id, Value = val, Description = GrblSetting.DescribeId(id) });
        }
        return settings;
    }

    /// <summary>Write one GRBL setting: sends $<paramref name="id"/>=<paramref name="value"/>.</summary>
    public async Task WriteSettingAsync(int id, string value, CancellationToken ct = default) =>
        await SendAndWaitAsync($"${id}={value}", ct);

    /// <summary>
    /// Stream G-code with the GRBL character-counting protocol. Fires
    /// <paramref name="onProgress"/> after each acknowledged line.
    /// Throws <see cref="OperationCanceledException"/> if <paramref name="ct"/> fires,
    /// or <see cref="InvalidOperationException"/> on GRBL error response.
    /// </summary>
    public async Task RunGcodeAsync(
        IReadOnlyList<string> lines,
        Action<int, int> onProgress,
        CancellationToken ct)
    {
        // Only send non-blank lines; comments still consume buffer if present.
        var sendable = lines
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToList();

        int total = sendable.Count;
        int done = 0;

        var inFlight = new Queue<int>();
        int inFlightBytes = 0;

        // Flush any leftover responses before we start.
        while (_responses.Reader.TryRead(out _)) { }

        foreach (var line in sendable)
        {
            ct.ThrowIfCancellationRequested();

            int needed = line.Length + 1; // +1 for '\n'

            // Wait for buffer room.
            while (inFlightBytes + needed > GrblBuffer)
            {
                var resp = await _responses.Reader.ReadAsync(ct);
                if (resp.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"GRBL error at line {done + 1}: {resp}");
                if (inFlight.TryDequeue(out var freed))
                    inFlightBytes -= freed;
            }

            await SendQueuedAsync(line + "\n", ct);
            inFlight.Enqueue(needed);
            inFlightBytes += needed;
            done++;
            onProgress(done, total);
        }

        // Drain remaining ok responses.
        while (inFlight.Count > 0)
        {
            var resp = await _responses.Reader.ReadAsync(ct);
            if (resp.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"GRBL error: {resp}");
            if (inFlight.TryDequeue(out var freed))
                inFlightBytes -= freed;
        }
    }

    // ── Internal loops ────────────────────────────────────────────────────

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            SerialPort? port;
            lock (_gate) { port = _port; }
            if (port is null) break;

            string line;
            try
            {
                line = await Task.Run(() => port.ReadLine(), ct);
                line = line.Trim('\r', '\n', ' ');
            }
            catch (OperationCanceledException) { break; }
            catch (TimeoutException) { continue; }
            catch
            {
                lock (_gate) { _state = MachineConnectionState.Error; }
                Publish(MachineConnectionState.Error, "Error", 0, 0, 0);
                break;
            }

            if (line.Length == 0) continue;

            if (line[0] == '<')
            {
                // Status report
                if (TryParseStatus(line, out var state, out var x, out var y, out var z))
                    Publish(MachineConnectionState.Connected, state, x, y, z);
            }
            else if (line.Equals("ok", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
            {
                if (line.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_gate) { _state = MachineConnectionState.Connected; }
                    Publish(MachineConnectionState.Connected, line.Split(':')[0], 0, 0, 0);
                }
                // When capturing settings, clear the capture buffer first so
                // ReadSettingsAsync can safely read from it after getting 'ok'.
                _settingsCapture = null;
                _responses.Writer.TryWrite(line);
            }
            else if (line.Length > 2 && line[0] == '$' && _settingsCapture is { } cap)
            {
                // $N=value lines produced by the $$ command.
                cap.Add(line);
            }
            // Other lines (startup greeting, etc.) are ignored.
        }
    }

    private async Task PollSenderAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(PollInterval);
            while (await timer.WaitForNextTickAsync(ct))
            {
                // Best-effort: skip tick if write lock is held by gcode streamer.
                if (await _writeLock.WaitAsync(50, ct))
                {
                    try
                    {
                        SerialPort? port;
                        lock (_gate) { port = _port; }
                        port?.Write("?\n");
                    }
                    finally { _writeLock.Release(); }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── Send helpers ──────────────────────────────────────────────────────

    /// <summary>Locked write for normal commands/gcode lines.</summary>
    private async Task SendQueuedAsync(string text, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try { GetPort().Write(text); }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Send + wait for one ok/error. Flushes stale responses first to stay in
    /// sync (one command in flight at a time from the manager's perspective).
    /// </summary>
    private async Task SendAndWaitAsync(string cmd, CancellationToken ct)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            // Flush any stale responses so we read exactly our command's response.
            while (_responses.Reader.TryRead(out _)) { }
            GetPort().Write(cmd + "\n");
        }
        finally { _writeLock.Release(); }

        var resp = await _responses.Reader.ReadAsync(ct);
        if (resp.StartsWith("error:", StringComparison.OrdinalIgnoreCase)
            || resp.StartsWith("ALARM:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"GRBL: {resp}");
    }

    /// <summary>Real-time byte — sent without the write lock (GRBL processes immediately).</summary>
    private void SendRealtime(string bytes)
    {
        SerialPort? port;
        lock (_gate) { port = _port; }
        port?.Write(bytes);
    }

    private SerialPort GetPort()
    {
        lock (_gate)
        {
            if (_port is null) throw new InvalidOperationException("Serial port is not open.");
            return _port;
        }
    }

    // ── Status parsing ────────────────────────────────────────────────────

    private static bool TryParseStatus(
        string line, out string state, out double x, out double y, out double z)
    {
        state = "Idle"; x = y = z = 0;
        if (line.Length < 3 || line[0] != '<') return false;
        var stateEnd = line.IndexOfAny(['|', '>', ':'], 1);
        state = stateEnd > 1 ? line[1..stateEnd] : "Unknown";
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
        var seg = end < 0 ? line[start..] : line[start..end];
        var parts = seg.Split(',');
        return parts.Length >= 3
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z);
    }

    // ── Status publishing ─────────────────────────────────────────────────

    private void Publish(MachineConnectionState connState, string machState,
        double x, double y, double z)
    {
        MachineStatus s;
        lock (_gate) { s = Snapshot(connState, machState, x, y, z); _lastStatus = s; }
        StatusChanged?.Invoke(this, s);
    }

    private MachineStatus Snapshot(MachineConnectionState connState, string machState,
        double x, double y, double z) =>
        new(connState, machState, x, y, z, DateTimeOffset.UtcNow,
            Interlocked.Increment(ref _seq));

    private static string Num(double v) =>
        v.ToString("F3", CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}
