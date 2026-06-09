using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Machine;

/// <summary>
/// Background service that pushes a machine-status heartbeat to all connected
/// SignalR clients on a fixed interval. This is the skeleton's "is everything
/// wired up?" signal; in later milestones the cadence/source becomes the real
/// machine's status reports.
/// </summary>
public sealed class HeartbeatBroadcaster : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

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
        _logger.LogInformation("Heartbeat broadcaster started (every {Seconds}s).", Interval.TotalSeconds);
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var status = _machine.GetStatus();
                await _hub.Clients.All.SendAsync(MachineHub.StatusEvent, status, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
