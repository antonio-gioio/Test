using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AisStream.Api.Tests;

/// <summary>
/// Boots the real API for integration tests against a PostGIS database. Background
/// services (AIS worker, simulator, persistence) are removed so tests are deterministic
/// and fast; the HTTP surface, auth, and tier enforcement are exercised for real.
///
/// Set the TEST_POSTGRES environment variable to point at a test database; otherwise it
/// falls back to the local development database.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("TEST_POSTGRES")
        ?? "Host=localhost;Port=5432;Database=aisstream;Username=postgres;Password=ais";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Note: Jwt:Key is intentionally NOT overridden here. The validation key is
            // read eagerly in Program.cs while token signing reads it lazily via IOptions;
            // overriding through the test config layer would desync them. The appsettings
            // default key is consistent for both and is allowed outside Production.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = ConnectionString,
                ["AisStream:ApiKey"] = "", // simulation mode (but services are removed anyway)
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHostedService>();
        });
    }
}
