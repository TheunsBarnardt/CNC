namespace Backend.Machine;

public enum JobLogEvent
{
    Started,
    Progress,
    FeedHold,
    Resumed,
    Completed,
    Error,
    Stopped,
}

/// <summary>
/// A single timestamped event in a job's execution log. Position (X/Y/Z) is
/// captured from the last known machine position at the time of the event.
/// </summary>
public sealed record JobLogEntry(
    DateTimeOffset Timestamp,
    JobLogEvent Event,
    int? LineNumber = null,
    int? LineTotal = null,
    double? X = null,
    double? Y = null,
    double? Z = null,
    string? Message = null);
