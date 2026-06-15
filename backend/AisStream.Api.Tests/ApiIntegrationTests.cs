using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

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
