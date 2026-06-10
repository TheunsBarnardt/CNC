using System.IO.Ports;
using Backend.Machine;
using Backend.Services;

namespace Backend.Api;

public static class MachineApi
{
    public static void MapMachineApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/machine");

        // ── Connection management ─────────────────────────────────────────

        group.MapGet("/ports", () =>
        {
            try { return Results.Ok(SerialPort.GetPortNames().OrderBy(p => p)); }
            catch { return Results.Ok(Array.Empty<string>()); }
        });

        group.MapGet("/connection", (MachineConnectionManager manager) => Results.Ok(new
        {
            isConnected = manager.IsSerialConnected,
            port = manager.ConnectedPort,
            baud = manager.ConnectedBaud,
            isJobRunning = manager.IsJobRunning,
        }));

        group.MapPost("/connect", async (ConnectRequest req, MachineConnectionManager manager) =>
        {
            if (string.IsNullOrWhiteSpace(req.Port))
                return Results.BadRequest(new { error = "port is required" });
            try
            {
                await manager.ConnectSerialAsync(req.Port.Trim(), req.Baud ?? 115200);
                return Results.Ok(new { message = $"Connected to {req.Port}." });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 502, title: "Serial connect failed");
            }
        });

        group.MapPost("/disconnect", async (MachineConnectionManager manager) =>
        {
            await manager.DisconnectSerialAsync();
            return Results.Ok(new { message = "Disconnected." });
        });

        group.MapGet("/status", (MachineConnectionManager manager) =>
            Results.Ok(manager.GetStatus()));

        // ── Motion commands ───────────────────────────────────────────────

        /// Jog one axis by a relative distance at the given feed rate.
        /// Body: { "axis": "X", "distance": 10.0, "feed": 1000.0 }
        group.MapPost("/jog", async (JogRequest req, MachineConnectionManager manager) =>
        {
            if (string.IsNullOrWhiteSpace(req.Axis)
                || req.Axis.Length != 1
                || req.Feed <= 0)
                return Results.BadRequest(new { error = "axis (X/Y/Z), distance, and feed > 0 are required." });

            try
            {
                await manager.JogAsync(req.Axis, req.Distance, req.Feed);
                return Results.Ok();
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 502); }
        });

        /// Start the homing cycle. Returns 202 immediately; watch SignalR for completion.
        group.MapPost("/home", (MachineConnectionManager manager) =>
        {
            try
            {
                _ = manager.StartHomeAsync(); // fire and forget; errors surface via SignalR
                return Results.Accepted();
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        /// Set work coordinate zero at the current position.
        group.MapPost("/zero", async (MachineConnectionManager manager) =>
        {
            try
            {
                await manager.SetZeroAsync();
                return Results.Ok();
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
            catch (Exception ex) { return Results.Problem(ex.Message, statusCode: 502); }
        });

        // ── Job streaming ─────────────────────────────────────────────────

        /// Start streaming the last generated G-code. Returns 202 immediately.
        /// Requires a prior call to POST /api/project/gcode to cache the program.
        group.MapPost("/run", (MachineConnectionManager manager, ProjectService projects) =>
        {
            var lines = projects.GetCachedGcode();
            if (lines is null or { Count: 0 })
                return Results.BadRequest(new { error = "No G-code available. Generate toolpath first." });

            try
            {
                if (!manager.StartJob(lines))
                    return Results.Conflict(new { error = "A job is already running." });
                return Results.Accepted(null, new { message = "Job started.", total = lines.Count });
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        /// Real-time feed hold '!'. The machine decelerates and holds.
        group.MapPost("/feed-hold", (MachineConnectionManager manager) =>
        {
            try { manager.FeedHold(); return Results.Ok(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        /// Resume from feed hold '~'.
        group.MapPost("/resume", (MachineConnectionManager manager) =>
        {
            try { manager.Resume(); return Results.Ok(); }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        /// Soft reset Ctrl-X: stops all motion immediately. Requires $X unlock after.
        /// Also cancels any streaming job.
        group.MapPost("/stop", async (MachineConnectionManager manager) =>
        {
            try
            {
                await manager.StopJobAsync(); // cancel streaming first
                manager.SoftReset();          // then tell the machine to stop
                return Results.Ok();
            }
            catch (InvalidOperationException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });
    }

    public sealed record ConnectRequest(string? Port, int? Baud);
    public sealed record JogRequest(string? Axis, double Distance, double Feed);
}
