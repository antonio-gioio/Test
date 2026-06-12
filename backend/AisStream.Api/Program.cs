using AisStream.Api.Hubs;
using AisStream.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AisStreamOptions>(
    builder.Configuration.GetSection(AisStreamOptions.SectionName));

builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.AddSingleton<VesselStore>();
builder.Services.AddSingleton<VesselBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<VesselBroadcaster>());
builder.Services.AddHostedService<AisStreamWorker>();
builder.Services.AddHostedService<VesselSimulator>();
builder.Services.AddHostedService<VesselPruner>();

// The Angular dev server runs on a different origin; SignalR needs credentials allowed.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseCors();
app.MapControllers();
app.MapHub<VesselHub>("/hubs/vessels");

app.Run();
