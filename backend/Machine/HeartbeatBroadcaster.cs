using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Machine;

/// <summary>
/// Pushes machine status to all SignalR clients.
/// Two delivery paths:
///  1. Immediate — via StatusChanged event (fires on every GRBL status report,
///     jog acknowledgement, or job-progress update; ~200 ms cadence when connected).
///  2. Heartbeat — PeriodicTimer every 2 s (keeps the stream alive when the machine
///     is idle and no events fire).
/// </summary>
public sealed class HeartbeatBroadcaster : BackgroundService
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(2);

    private readonly IHubContext<MachineHub> _hub;
    private readonly IMachineConnection _machine;
    private readonly ILogger<HeartbeatBroadcaster> _logger;

    public HeartbeatBroadcaster(
        IHubContext<MachineHub> hub,
        IMachineConnection machine,
        ILogger<HeartbeatBroadcaster> logger)
    {
        _hub = hub;
        _machine = machine;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatBroadcaster started.");
        _machine.StatusChanged += OnStatusChanged;
        try
        {
            using var timer = new PeriodicTimer(HeartbeatInterval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Periodic push — keeps clients alive when machine is truly idle.
                await PushAsync(_machine.GetStatus(), stoppingToken);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            _machine.StatusChanged -= OnStatusChanged;
        }
    }

    private async void OnStatusChanged(object? sender, MachineStatus status)
    {
        // Fire-and-forget push. Errors here don't crash the broadcaster.
        try { await PushAsync(status, CancellationToken.None); }
        catch { /* client disconnects are normal */ }
    }

    private Task PushAsync(MachineStatus status, CancellationToken ct) =>
        _hub.Clients.All.SendAsync(MachineHub.StatusEvent, status, ct);
}
