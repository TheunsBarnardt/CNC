namespace Backend.Machine;

/// <summary>
/// Abstraction over a machine link (GRBL serial, or a fake for testing).
///
/// Per the architecture in PLASMA_CAM_PLAN.md, all serial/hardware access sits
/// behind this interface so the whole app is testable without hardware. A real
/// <c>SerialMachineConnection</c> arrives in Milestone 3; until then
/// <see cref="FakeMachineConnection"/> is the only implementation.
///
/// SAFETY: implementations must never initiate machine motion as a side effect
/// of connecting or reporting status. Motion is added behind explicit,
/// deliberate calls in later milestones.
/// </summary>
public interface IMachineConnection
{
    /// <summary>Current state of the link.</summary>
    MachineConnectionState State { get; }

    /// <summary>Returns the latest known status snapshot.</summary>
    MachineStatus GetStatus();

    /// <summary>Raised whenever a new status snapshot is available.</summary>
    event EventHandler<MachineStatus>? StatusChanged;

    /// <summary>Establish the link. No motion is performed.</summary>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>Tear down the link.</summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
