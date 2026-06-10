using System.IO.Ports;
using Backend.Machine;

namespace Backend.Api;

public static class MachineApi
{
    public static void MapMachineApi(this WebApplication app)
    {
        var group = app.MapGroup("/api/machine");

        // Available serial port names (OS-reported). Returns empty array when
        // System.IO.Ports is not available (non-Windows build, etc.).
        group.MapGet("/ports", () =>
        {
            try { return Results.Ok(SerialPort.GetPortNames().OrderBy(p => p)); }
            catch { return Results.Ok(Array.Empty<string>()); }
        });

        // Current connection state (REST snapshot; live updates come via SignalR).
        group.MapGet("/connection", (MachineConnectionManager manager) => Results.Ok(new
        {
            isConnected = manager.IsSerialConnected,
            port = manager.ConnectedPort,
            baud = manager.ConnectedBaud,
        }));

        // Establish a real GRBL serial link.
        group.MapPost("/connect", async (ConnectRequest req, MachineConnectionManager manager) =>
        {
            if (string.IsNullOrWhiteSpace(req.Port))
                return Results.BadRequest(new { error = "port is required" });
            int baud = req.Baud ?? 115200;
            try
            {
                await manager.ConnectSerialAsync(req.Port.Trim(), baud);
                return Results.Ok(new { message = $"Connected to {req.Port} at {baud} baud." });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: StatusCodes.Status502BadGateway,
                    title: "Serial connect failed");
            }
        });

        // Tear down the serial link and revert to the fake connection.
        group.MapPost("/disconnect", async (MachineConnectionManager manager) =>
        {
            await manager.DisconnectSerialAsync();
            return Results.Ok(new { message = "Disconnected." });
        });

        // REST fallback for the current status snapshot.
        group.MapGet("/status", (MachineConnectionManager manager) =>
            Results.Ok(manager.GetStatus()));
    }

    public sealed record ConnectRequest(string? Port, int? Baud);
}
