using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AisStream.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AisStream.Api.Tests;

public class ApiIntegrationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ApiIntegrationTests(ApiFactory factory) => _factory = factory;

    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.com";

    private async Task<(HttpClient Client, string Token)> RegisteredClientAsync()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "Password123" });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return (client, token);
    }

    private async Task<string> AdminTokenAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@aisstream.local", password = "Admin123!" });
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    private static HttpRequestMessage Get(string url, string token) => Authed(HttpMethod.Get, url, token);

    [Fact]
    public async Task Seeded_admin_can_reach_admin_endpoints()
    {
        var client = _factory.CreateClient();
        var token = await AdminTokenAsync(client);

        var users = await client.SendAsync(Get("/api/admin/users", token));
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);

        var stats = await client.SendAsync(Get("/api/admin/stats", token));
        Assert.Equal(HttpStatusCode.OK, stats.StatusCode);
        var body = await stats.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("totalUsers").GetInt32() >= 1);
    }

    [Fact]
    public async Task Non_admin_is_forbidden_from_admin_endpoints()
    {
        var (client, token) = await RegisteredClientAsync();
        var res = await client.SendAsync(Get("/api/admin/users", token));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Account_me_reports_admin_flag()
    {
        var client = _factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var me = await client.SendAsync(Get("/api/account/me", token));
        var body = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isAdmin").GetBoolean());
    }

    [Fact]
    public async Task Watch_area_create_list_and_delete()
    {
        var (client, token) = await RegisteredClientAsync();

        var create = await client.SendAsync(Authed(HttpMethod.Post, "/api/watch-areas", token,
            JsonContent.Create(new { name = "Channel", latMin = 50, lonMin = -2, latMax = 51, lonMax = 0 })));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var list = await client.SendAsync(Get("/api/watch-areas", token));
        var areas = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, areas.GetArrayLength());

        var matches = await client.SendAsync(Get("/api/watch-areas/matches", token));
        Assert.Equal(HttpStatusCode.OK, matches.StatusCode);

        var del = await client.SendAsync(Authed(HttpMethod.Delete, $"/api/watch-areas/{id}", token));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);
    }

    [Fact]
    public async Task Admin_can_crud_integrations()
    {
        var client = _factory.CreateClient();
        var token = await AdminTokenAsync(client);

        var types = await client.SendAsync(Get("/api/admin/provider-types", token));
        Assert.Equal(HttpStatusCode.OK, types.StatusCode);

        var name = $"itest-{Guid.NewGuid():N}";
        var create = await client.SendAsync(Authed(HttpMethod.Post, "/api/admin/integrations", token,
            JsonContent.Create(new
            {
                name,
                provider = "Simulator",
                enabled = false,
                apiKey = (string?)null,
                url = (string?)null,
                boundingBoxesJson = (string?)null,
                mmsiFilterJson = (string?)null,
                pollSeconds = 60,
                centerLat = 50.0,
                centerLon = -1.0,
                radiusKm = 100.0,
            })));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetInt32();
        Assert.Equal("Simulator", created.GetProperty("provider").GetString());

        var list = await client.SendAsync(Get("/api/admin/integrations", token));
        var items = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(items.EnumerateArray(), i => i.GetProperty("id").GetInt32() == id);

        var enable = await client.SendAsync(
            Authed(HttpMethod.Post, $"/api/admin/integrations/{id}/enabled?value=true", token));
        Assert.Equal(HttpStatusCode.NoContent, enable.StatusCode);

        var delete = await client.SendAsync(Authed(HttpMethod.Delete, $"/api/admin/integrations/{id}", token));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Admin_actions_are_audit_logged()
    {
        var client = _factory.CreateClient();
        var adminToken = await AdminTokenAsync(client);

        // Create a normal user, then change its tier as admin -> should be audited.
        var (_, userToken) = await RegisteredClientAsync();
        var userId = await UserIdFromMeAsync(client, userToken);

        var setTier = await client.SendAsync(Authed(HttpMethod.Post, $"/api/admin/users/{userId}/tier",
            adminToken, JsonContent.Create(new { tier = 1 })));
        Assert.Equal(HttpStatusCode.NoContent, setTier.StatusCode);

        var audit = await client.SendAsync(Get("/api/admin/audit", adminToken));
        var entries = await audit.Content.ReadFromJsonAsync<JsonElement>();
        var actions = entries.EnumerateArray().Select(e => e.GetProperty("action").GetString()).ToList();
        Assert.Contains("SetTier", actions);
    }

    private async Task<string> UserIdFromMeAsync(HttpClient client, string token)
    {
        // Admin user list includes the new user; find its id by email is overkill — use the
        // admin users endpoint to grab any non-admin id created in this test run.
        var res = await client.SendAsync(Get("/api/admin/users", await AdminTokenAsync(client)));
        var users = await res.Content.ReadFromJsonAsync<JsonElement>();
        foreach (var u in users.EnumerateArray())
        {
            if (!u.GetProperty("isAdmin").GetBoolean())
            {
                return u.GetProperty("id").GetString()!;
            }
        }

        throw new InvalidOperationException("No non-admin user found");
    }

    [Fact]
    public async Task Billing_webhook_is_inert_until_configured()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/billing/webhook",
            new { email = "x@y.com", tier = 2 });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);
    }

    [Fact]
    public async Task Billing_webhook_sets_tier_when_configured_with_the_secret()
    {
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:WebhookSecret"] = "test-secret",
            })));
        var client = factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = JsonContent.Create(new { email, tier = 2 }),
        };
        req.Headers.Add("X-Webhook-Secret", "test-secret");
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        // Wrong secret is rejected.
        var bad = new HttpRequestMessage(HttpMethod.Post, "/api/billing/webhook")
        {
            Content = JsonContent.Create(new { email, tier = 0 }),
        };
        bad.Headers.Add("X-Webhook-Secret", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(bad)).StatusCode);

        // The user's tier is now Enterprise.
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123" });
        var tier = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("tier").GetString();
        Assert.Equal("Enterprise", tier);
    }

    [Fact]
    public async Task History_endpoint_returns_a_snapshot()
    {
        var client = _factory.CreateClient();
        var at = Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("o"));
        var res = await client.GetAsync(
            $"/api/vessels/history?latMin=49&lonMin=-3&latMax=51&lonMax=-1&at={at}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("vessels", out _));
        Assert.True(body.TryGetProperty("count", out _));
    }

    [Fact]
    public async Task Follow_and_unfollow_via_rest_update_the_list()
    {
        var (client, token) = await RegisteredClientAsync();

        var follow = await client.SendAsync(Authed(HttpMethod.Put, "/api/account/followed/123456789", token));
        Assert.Equal(HttpStatusCode.OK, follow.StatusCode);
        var afterFollow = await follow.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(afterFollow.EnumerateArray().Select(e => e.GetInt64()), m => m == 123456789);

        var unfollow = await client.SendAsync(Authed(HttpMethod.Delete, "/api/account/followed/123456789", token));
        Assert.Equal(HttpStatusCode.OK, unfollow.StatusCode);
        var afterUnfollow = await unfollow.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(afterUnfollow.EnumerateArray().Select(e => e.GetInt64()), m => m == 123456789);
    }

    [Fact]
    public async Task Tier_rate_limit_returns_429_when_exceeded()
    {
        // Fresh host with a tiny Free-tier limit so the throttle is observable.
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimit:TierApiFree"] = "3" })));
        var client = factory.CreateClient();

        var codes = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            var res = await client.GetAsync("/api/vessels/nearest?lat=50&lon=-1&limit=1");
            codes.Add((int)res.StatusCode);
        }

        Assert.Contains(200, codes);
        Assert.Contains(429, codes);
    }

    [Fact]
    public async Task Nearest_endpoint_returns_a_list()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/vessels/nearest?lat=50.5&lon=-1&limit=5");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public async Task Export_returns_csv_with_a_header_row()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/vessels/export?latMin=49&lonMin=-3&latMax=51&lonMax=-1");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("text/csv", res.Content.Headers.ContentType?.MediaType);
        var csv = await res.Content.ReadAsStringAsync();
        Assert.StartsWith("mmsi,name,latitude", csv);
    }

    [Fact]
    public async Task Track_endpoint_returns_json_and_geojson()
    {
        var client = _factory.CreateClient();

        var json = await client.GetAsync("/api/vessels/200000000/track?hours=1");
        Assert.Equal(HttpStatusCode.OK, json.StatusCode);
        var body = await json.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("points", out _));

        var geo = await client.GetAsync("/api/vessels/200000000/track?hours=1&format=geojson");
        Assert.Equal(HttpStatusCode.OK, geo.StatusCode);
        Assert.Equal("application/geo+json", geo.Content.Headers.ContentType?.MediaType);
        var feature = await geo.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Feature", feature.GetProperty("type").GetString());
    }

    [Fact]
    public async Task Vessel_stats_endpoint_returns_breakdown()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/vessels/stats");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("total", out _));
        Assert.True(body.TryGetProperty("byShipType", out _));
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Register_then_login_issues_tokens()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();

        var register = await client.PostAsJsonAsync("/api/auth/register",
            new { email, password = "Password123" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email, password = "Password123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
        Assert.Equal("Free", body.GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Refresh_token_rotates_and_old_one_is_rejected()
    {
        var client = _factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "Password123" });
        var first = await reg.Content.ReadFromJsonAsync<JsonElement>();
        var refresh1 = first.GetProperty("refreshToken").GetString()!;

        // Use the refresh token -> new access + rotated refresh token.
        var r1 = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        var refreshed = await r1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrEmpty(refreshed.GetProperty("token").GetString()));
        Assert.NotEqual(refresh1, refreshed.GetProperty("refreshToken").GetString());

        // The old (rotated) refresh token is now rejected.
        var r2 = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh1 });
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
    }

    [Fact]
    public async Task Confirm_email_with_a_valid_token_marks_it_confirmed()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });

        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            Assert.False(await users.IsEmailConfirmedAsync(user!)); // not confirmed at registration
            token = await users.GenerateEmailConfirmationTokenAsync(user!);
        }

        var confirm = await client.PostAsJsonAsync("/api/auth/confirm-email", new { email, token });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            Assert.True(await users.IsEmailConfirmedAsync((await users.FindByEmailAsync(email))!));
        }
    }

    [Fact]
    public async Task Reset_password_rejects_an_invalid_token()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });
        var res = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { email, token = "bogus-token", newPassword = "NewPassword456" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Confirm_email_rejects_an_invalid_token()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });
        var res = await client.PostAsJsonAsync("/api/auth/confirm-email", new { email, token = "bogus-token" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Creating_a_duplicate_integration_name_conflicts()
    {
        var client = _factory.CreateClient();
        var token = await AdminTokenAsync(client);
        var body = new
        {
            name = $"dup-{Guid.NewGuid():N}",
            provider = "Simulator",
            enabled = false,
            apiKey = (string?)null,
            url = (string?)null,
            boundingBoxesJson = (string?)null,
            mmsiFilterJson = (string?)null,
            pollSeconds = 60,
            centerLat = 50.0,
            centerLon = -1.0,
            radiusKm = 100.0,
        };

        var first = await client.SendAsync(Authed(HttpMethod.Post, "/api/admin/integrations", token, JsonContent.Create(body)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var second = await client.SendAsync(Authed(HttpMethod.Post, "/api/admin/integrations", token, JsonContent.Create(body)));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        await client.SendAsync(Authed(HttpMethod.Delete, $"/api/admin/integrations/{id}", token));
    }

    [Fact]
    public async Task Forgot_password_always_returns_ok()
    {
        var client = _factory.CreateClient();
        var existing = await client.PostAsJsonAsync("/api/auth/forgot-password", new { email = NewEmail() });
        Assert.Equal(HttpStatusCode.OK, existing.StatusCode); // no user-enumeration leak
    }

    [Fact]
    public async Task Reset_password_with_a_valid_token_changes_the_password()
    {
        var client = _factory.CreateClient();
        var email = NewEmail();
        await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password123" });

        // Generate a real reset token via Identity (as the email flow would deliver).
        string token;
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync(email);
            token = await users.GeneratePasswordResetTokenAsync(user!);
        }

        var reset = await client.PostAsJsonAsync("/api/auth/reset-password",
            new { email, token, newPassword = "NewPassword456" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        // New password works; old one no longer does.
        var newLogin = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "NewPassword456" });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);
        var oldLogin = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password123" });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var client = _factory.CreateClient();
        var reg = await client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "Password123" });
        var refresh = (await reg.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("refreshToken").GetString()!;

        await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = refresh });

        var res = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_weak_password()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/register",
            new { email = NewEmail(), password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_is_Free_and_blocked_from_large_viewport()
    {
        var client = _factory.CreateClient();
        // area = 3 * 8 = 24 sq deg > Free limit of 4
        var res = await client.GetAsync("/api/vessels?latMin=49&lonMin=-5&latMax=52&lonMax=3");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_small_viewport_is_allowed()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/vessels?latMin=50&lonMin=-2&latMax=51&lonMax=0");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Account_me_requires_authentication()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/account/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Upgrading_to_Enterprise_unlocks_large_viewport()
    {
        var (client, token) = await RegisteredClientAsync();

        // Upgrade to Enterprise (tier = 2) -> new token with the new claim.
        var upgrade = await client.SendAsync(Authed(HttpMethod.Post, "/api/account/tier", token,
            JsonContent.Create(new { tier = 2 })));
        Assert.Equal(HttpStatusCode.OK, upgrade.StatusCode);
        var newToken = (await upgrade.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

        var big = await client.SendAsync(Authed(HttpMethod.Get,
            "/api/vessels?latMin=49&lonMin=-5&latMax=52&lonMax=3", newToken));
        Assert.Equal(HttpStatusCode.OK, big.StatusCode);
    }

    [Fact]
    public async Task Search_short_query_returns_empty()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/vessels/search?q=a");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Equal(0, body.GetArrayLength());
    }

    [Fact]
    public async Task Search_returns_a_json_array()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/vessels/search?q=MAERSK");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public async Task Clusters_endpoint_returns_aggregates()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/api/vessels/clusters?latMin=-90&lonMin=-180&latMax=90&lonMax=180&zoom=2");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("clusters", out _));
        Assert.True(body.GetProperty("cellDegrees").GetDouble() > 0);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string url, string token, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };
        req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return req;
    }
}
