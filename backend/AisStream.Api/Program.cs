using System.Text;
using System.Threading.RateLimiting;
using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Hubs;
using AisStream.Api.Infrastructure;
using AisStream.Api.Ingestion;
using AisStream.Api.Ingestion.Providers;
using AisStream.Api.Messaging;
using AisStream.Api.Services;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection(IngestionOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<BillingOptions>(builder.Configuration.GetSection(BillingOptions.SectionName));
builder.Services.Configure<ClusterOptions>(builder.Configuration.GetSection(ClusterOptions.SectionName));

// AIS data sources are configured per-integration in the database (admin-managed), built by
// the provider factory and run by the ingestion manager.
builder.Services.AddSingleton<IAisProviderFactory, AisProviderFactory>();
builder.Services.AddHttpClient();

var cluster = builder.Configuration.GetSection(ClusterOptions.SectionName).Get<ClusterOptions>() ?? new ClusterOptions();
var redis = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();

if (!redis.Enabled && cluster.Role != NodeRole.All)
{
    throw new InvalidOperationException(
        $"Cluster:Role is '{cluster.Role}' but no Redis:ConnectionString is configured. " +
        "Splitting the Ingestor and Web roles requires Redis to connect them.");
}

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=aisstream;Username=postgres;Password=ais";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite()));

builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

// Refuse to start in Production with the insecure default signing key.
if (builder.Environment.IsProduction() && jwt.Key.StartsWith("dev-only", StringComparison.Ordinal))
{
    throw new InvalidOperationException(
        "Jwt:Key is still the insecure development default. Set a strong key " +
        "(env var Jwt__Key) before running in Production.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
        };

        // SignalR delivers the token via the query string on the WebSocket handshake.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuditService>();

builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddSignalR(options =>
{
    // Tuned for many concurrent client connections.
    options.MaximumReceiveMessageSize = 64 * 1024;
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
    c.SwaggerDoc("v1", new() { Title = "AIS Vessel Tracker API", Version = "v1" }));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

// Throttle auth endpoints to blunt credential-stuffing / brute force. The limit is read from
// request services so test/host config overrides apply (eagerly-read config would not).
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
    {
        var limit = context.RequestServices.GetRequiredService<IConfiguration>()
            .GetValue("RateLimit:AuthPermitLimit", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
            });
    });

    // Per-tier throttle on heavy, explicitly-triggered endpoints (export/search/nearest).
    // Keyed per user (or IP for guests); Enterprise is effectively unlimited.
    options.AddPolicy("tierApi", context =>
    {
        var cfg = context.RequestServices.GetRequiredService<IConfiguration>();
        var tier = TokenService.TierOf(context.User);
        var limit = tier switch
        {
            SubscriptionTier.Enterprise => cfg.GetValue("RateLimit:TierApiEnterprise", 100_000),
            SubscriptionTier.Pro => cfg.GetValue("RateLimit:TierApiPro", 300),
            _ => cfg.GetValue("RateLimit:TierApiFree", 60),
        };
        var key = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString() ?? "anon";
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: $"{tier}:{key}",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = TimeSpan.FromMinutes(1),
            });
    });
});

// Vessel bus: Redis pub/sub when configured (multi-node), otherwise in-process (single node).
if (redis.Enabled)
{
    // AbortOnConnectFail=false: start even if Redis is briefly down and reconnect, so a Redis
    // blip doesn't crash the node at startup.
    var redisConfig = ConfigurationOptions.Parse(redis.ConnectionString);
    redisConfig.AbortOnConnectFail = false;
    builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConfig));
    builder.Services.AddSingleton<IVesselBus, RedisVesselBus>();
    builder.Services.AddStackExchangeRedisCache(o => o.ConfigurationOptions = redisConfig);
}
else
{
    builder.Services.AddSingleton<IVesselBus, InProcessVesselBus>();
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSingleton<VesselStore>();
builder.Services.AddSingleton<VesselBroadcaster>();

// Every node consumes the bus to keep its cache warm; web nodes also fan out to SignalR.
builder.Services.AddHostedService<VesselBusConsumer>();

if (cluster.RunsRealtime)
{
    builder.Services.AddHostedService(sp => sp.GetRequiredService<VesselBroadcaster>());
    builder.Services.AddHostedService<WatchAreaAlertService>();
    builder.Services.AddHostedService<FollowedBroadcastService>();
}

// Only the ingestor runs the configured integrations and writes to the database.
if (cluster.RunsIngestion)
{
    builder.Services.AddHostedService<VesselPersistenceService>();
    builder.Services.AddHostedService<VesselPruner>();
    builder.Services.AddHostedService<IngestionManager>();
}

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

// Apply migrations on startup (ingestor only — it owns the schema; web nodes just query it).
// Retry briefly so the API can start alongside a database that is still booting.
if (cluster.RunsIngestion)
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            db.Database.Migrate();
            break;
        }
        catch (Exception ex) when (attempt < 10)
        {
            migrationLogger.LogWarning(ex, "Database not ready (attempt {Attempt}); retrying in 3s", attempt);
            Thread.Sleep(TimeSpan.FromSeconds(3));
        }
    }

    await DataSeeder.SeedAsync(scope.ServiceProvider, migrationLogger);
}

app.UseExceptionHandler();

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "AIS Vessel Tracker API v1"));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors();
app.UseRateLimiter();

// Prometheus request metrics (duration, count, in-flight) on every HTTP request.
app.UseHttpMetrics();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<VesselHub>("/hubs/vessels");
app.MapHealthChecks("/health");
// k8s-style probes: liveness has no dependencies; readiness checks the database.
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,
});
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
});
app.MapMetrics(); // /metrics for Prometheus scraping

app.Run();

public partial class Program;
