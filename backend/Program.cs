using System.Text.Json.Serialization;
using Backend.Api;
using Backend.Hubs;
using Backend.Import;
using Backend.Import.Dxf;
using Backend.Import.Svg;
using Backend.Machine;
using Backend.Post;
using Backend.Services;

var builder = WebApplication.CreateBuilder(args);

// Frontend dev origins: any localhost port (Vite picks a free one when 5173
// is busy). Loopback-only, so nothing off-machine is allowed; production
// packaging (single desktop shell) will revisit this.
const string FrontendCors = "frontend-dev";

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCors, policy => policy
        .SetIsOriginAllowed(origin =>
            Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()); // required for SignalR (WebSockets carry credentials)
});

builder.Services
    .AddSignalR()
    // Serialize enums as their string names so clients see "Connected", not 2.
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Machine link: MachineConnectionManager starts with FakeMachineConnection and can
// swap to a real SerialMachineConnection at runtime via the /api/machine/* endpoints.
builder.Services.AddSingleton<FakeMachineConnection>();
builder.Services.AddSingleton<MachineConnectionManager>();
builder.Services.AddSingleton<IMachineConnection>(sp =>
    sp.GetRequiredService<MachineConnectionManager>());
builder.Services.AddHostedService<HeartbeatBroadcaster>();

// Project state + vector file import.
builder.Services.AddSingleton<ProjectService>();
builder.Services.AddSingleton<IFileImporter, SvgImporter>();
builder.Services.AddSingleton<IFileImporter, DxfImporter>();
builder.Services.AddSingleton<FileImportService>();

// Post-processors: neutral toolpath → controller dialect. Pluggable — a new
// dialect is one IPostProcessor registration here.
builder.Services.AddSingleton<IPostProcessor, GrblPlasmaPostProcessor>();
builder.Services.AddSingleton<IPostProcessor, GrblLaserPostProcessor>();
builder.Services.AddSingleton<PostProcessorRegistry>();

// Material profiles: app-level presets persisted under LocalAppData.
builder.Services.AddSingleton<ProfileStore>();

// Job checkpoints: power-loss recovery persisted under LocalAppData.
builder.Services.AddSingleton<CheckpointService>();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors(FrontendCors);

// Simple liveness endpoint the frontend pings to confirm the backend is up.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "diy-grbl-cam-backend",
    timeUtc = DateTimeOffset.UtcNow,
}));

app.MapProjectApi();
app.MapProfileApi();
app.MapMachineApi();
app.MapHub<MachineHub>("/hubs/machine");

app.Run();
