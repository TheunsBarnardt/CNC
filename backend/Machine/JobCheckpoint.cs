namespace Backend.Machine;

/// <summary>
/// Persisted snapshot of a running job — written to disk so a power-loss or
/// crash recovery can resume from a safe point (the start of the last
/// interrupted cut, not the middle of it).
/// </summary>
public sealed record JobCheckpoint(
    string JobId,
    DateTimeOffset StartedAt,
    DateTimeOffset CheckpointAt,
    int LastLineDone,
    int TotalLines,
    double LastX,
    double LastY,
    double LastZ,
    IReadOnlyList<string> GcodeLines);
