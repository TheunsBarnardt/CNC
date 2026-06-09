using Backend.Machine;
using Microsoft.AspNetCore.SignalR;

namespace Backend.Hubs;

/// <summary>
/// SignalR hub that streams live machine status to connected clients.
///
/// Status is pushed by <see cref="HeartbeatBroadcaster"/> via
/// <c>IHubContext&lt;MachineHub&gt;</c>; this class also lets a client pull the
/// current snapshot on demand (e.g. right after connecting).
/// </summary>
public sealed class MachineHub : Hub
{
    /// <summary>The client method name the server invokes with each status.</summary>
    public const string StatusEvent = "machineStatus";

    private readonly IMachineConnection _machine;

    public MachineHub(IMachineConnection machine) => _machine = machine;

    /// <summary>Send the current status to the caller only.</summary>
    public Task<MachineStatus> RequestStatus() => Task.FromResult(_machine.GetStatus());

    public override async Task OnConnectedAsync()
    {
        // Give a freshly-connected client an immediate snapshot rather than
        // making it wait for the next heartbeat tick.
        await Clients.Caller.SendAsync(StatusEvent, _machine.GetStatus());
        await base.OnConnectedAsync();
    }
}
