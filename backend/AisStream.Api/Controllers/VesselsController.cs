using AisStream.Api.Models;
using AisStream.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AisStream.Api.Controllers;

[ApiController]
[Route("api/vessels")]
public class VesselsController : ControllerBase
{
    private readonly VesselStore _store;
    private readonly AisStreamOptions _options;

    public VesselsController(VesselStore store, IOptions<AisStreamOptions> options)
    {
        _store = store;
        _options = options.Value;
    }

    /// <summary>Snapshot of every vessel currently tracked, for initial page load.</summary>
    [HttpGet]
    public IReadOnlyList<Vessel> GetAll() =>
        _store.Snapshot(TimeSpan.FromMinutes(_options.VesselTtlMinutes));

    /// <summary>Reports whether the live aisstream.io feed or the simulator is active.</summary>
    [HttpGet("/api/status")]
    public object GetStatus() => new
    {
        Mode = string.IsNullOrWhiteSpace(_options.ApiKey) ? "simulation" : "live",
        VesselCount = _store.Snapshot(TimeSpan.FromMinutes(_options.VesselTtlMinutes)).Count,
    };
}
