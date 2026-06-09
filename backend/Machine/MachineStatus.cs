namespace Backend.Machine;

/// <summary>
/// Connection state of a machine link. Distinct from the machine's *motion*
/// state (idle/run/hold) so the UI can tell "no link" apart from "linked but idle".
/// </summary>
public enum MachineConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error,
}

/// <summary>
/// A single immutable status snapshot from a machine connection.
///
/// This is the neutral shape the rest of the app consumes; a real GRBL
/// connection (added in Milestone 3) will populate the same record from
/// parsed status reports, so nothing downstream needs to know whether the
/// data came from real hardware or <see cref="FakeMachineConnection"/>.
/// </summary>
/// <param name="ConnectionState">State of the link itself.</param>
/// <param name="MachineState">
/// GRBL-style motion state string (e.g. "Idle", "Run", "Hold", "Alarm").
/// </param>
/// <param name="X">Work-coordinate X position.</param>
/// <param name="Y">Work-coordinate Y position.</param>
/// <param name="Z">Work-coordinate Z position.</param>
/// <param name="Timestamp">When this snapshot was produced.</param>
/// <param name="HeartbeatSequence">
/// Monotonically increasing counter so the UI can prove the stream is live.
/// </param>
public sealed record MachineStatus(
    MachineConnectionState ConnectionState,
    string MachineState,
    double X,
    double Y,
    double Z,
    DateTimeOffset Timestamp,
    long HeartbeatSequence);
