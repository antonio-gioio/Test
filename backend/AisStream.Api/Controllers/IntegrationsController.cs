using System.ComponentModel.DataAnnotations;
using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Ingestion;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Controllers;

/// <summary>Admin-managed AIS data-source integrations (CRUD). Changes take effect within ~10s.</summary>
[ApiController]
[Route("api/admin/integrations")]
[Authorize(Roles = Roles.Admin)]
public class IntegrationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public IntegrationsController(AppDbContext db) => _db = db;

    public record IntegrationDto(
        int Id, string Name, AisProviderType Provider, bool Enabled,
        string? ApiKey, string? Url, string? BoundingBoxesJson, string? MmsiFilterJson,
        int PollSeconds, double CenterLat, double CenterLon, double RadiusKm);

    public record SaveIntegration(
        [Required] string Name, AisProviderType Provider, bool Enabled,
        string? ApiKey, string? Url, string? BoundingBoxesJson, string? MmsiFilterJson,
        int PollSeconds, double CenterLat, double CenterLon, double RadiusKm);

    /// <summary>The selectable provider types and which fields each one uses (for the admin UI).</summary>
    [HttpGet("/api/admin/provider-types")]
    public ActionResult<object> ProviderTypes() => Ok(new[]
    {
        Describe(AisProviderType.Simulator, false, false, "Built-in fake fleet (no config)"),
        Describe(AisProviderType.AisStream, true, true, "Free terrestrial WebSocket feed"),
        Describe(AisProviderType.Digitraffic, false, true, "Free open MQTT feed (Baltic)"),
        Describe(AisProviderType.MarineTraffic, true, true, "Paid, free trial; polls a bounding box"),
        Describe(AisProviderType.Datalastic, true, true, "Paid, free trial; polls a search circle"),
    });

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<IntegrationDto>>> List()
    {
        var items = await _db.Integrations.OrderBy(i => i.Name).ToListAsync();
        return Ok(items.Select(ToDto).ToList());
    }

    [HttpPost]
    public async Task<ActionResult<IntegrationDto>> Create(SaveIntegration body)
    {
        if (await _db.Integrations.AnyAsync(i => i.Name == body.Name))
        {
            return Conflict(new { error = "An integration with that name already exists." });
        }

        var entity = new Integration();
        Apply(entity, body);
        _db.Integrations.Add(entity);
        await _db.SaveChangesAsync();
        return Ok(ToDto(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<IntegrationDto>> Update(int id, SaveIntegration body)
    {
        var entity = await _db.Integrations.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        Apply(entity, body);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        entity.Revision++; // signal the ingestion manager to restart this runner
        await _db.SaveChangesAsync();
        return Ok(ToDto(entity));
    }

    [HttpPost("{id:int}/enabled")]
    public async Task<IActionResult> SetEnabled(int id, [FromQuery] bool value)
    {
        var entity = await _db.Integrations.FindAsync(id);
        if (entity is null)
        {
            return NotFound();
        }

        entity.Enabled = value;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var removed = await _db.Integrations.Where(i => i.Id == id).ExecuteDeleteAsync();
        return removed > 0 ? NoContent() : NotFound();
    }

    private static void Apply(Integration e, SaveIntegration b)
    {
        e.Name = b.Name.Trim();
        e.Provider = b.Provider;
        e.Enabled = b.Enabled;
        e.ApiKey = b.ApiKey;
        e.Url = b.Url;
        e.BoundingBoxesJson = b.BoundingBoxesJson;
        e.MmsiFilterJson = b.MmsiFilterJson;
        e.PollSeconds = b.PollSeconds <= 0 ? 60 : b.PollSeconds;
        e.CenterLat = b.CenterLat;
        e.CenterLon = b.CenterLon;
        e.RadiusKm = b.RadiusKm <= 0 ? 100 : b.RadiusKm;
    }

    private static IntegrationDto ToDto(Integration i) => new(
        i.Id, i.Name, i.Provider, i.Enabled, i.ApiKey, i.Url, i.BoundingBoxesJson,
        i.MmsiFilterJson, i.PollSeconds, i.CenterLat, i.CenterLon, i.RadiusKm);

    private static object Describe(AisProviderType type, bool usesApiKey, bool usesUrl, string note) =>
        new { value = type.ToString(), usesApiKey, usesUrl, note };
}
