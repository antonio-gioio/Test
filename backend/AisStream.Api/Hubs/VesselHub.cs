using System.Collections.Concurrent;
using System.Security.Claims;
using AisStream.Api.Auth;
using AisStream.Api.Data;
using AisStream.Api.Models;
using AisStream.Api.Services;
using AisStream.Api.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AisStream.Api.Hubs;

public record ViewportResult(bool Accepted, string? Message, IReadOnlyList<Vessel> Vessels);

/// <summary>
/// SignalR hub for live vessel updates. Clients call <see cref="SubscribeViewport"/> with
/// their current map bounds; the server validates the request against the caller's tier,
/// returns a warm snapshot from the cache, and joins the connection to per-tile groups so
/// it then receives only deltas for that area, at its tier's refresh cadence.
/// Anonymous connections are treated as the Free tier.
/// </summary>
[AllowAnonymous]
public class VesselHub : Hub
{
    private static readonly ConcurrentDictionary<string, HashSet<string>> ConnectionGroups = new();

    private readonly VesselStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VesselHub> _logger;

    public VesselHub(VesselStore store, IServiceScopeFactory scopeFactory, ILogger<VesselHub> logger)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ViewportResult> SubscribeViewport(double latMin, double lonMin, double latMax, double lonMax)
    {
        var tier = TokenService.TierOf(Context.User);
        var limits = TierLimits.For(tier);
        var bounds = new Bounds(latMin, lonMin, latMax, lonMax);

        if (bounds.AreaSqDegrees > limits.MaxViewportAreaSqDegrees)
        {
            return new ViewportResult(
                Accepted: false,
                Message: $"Your {tier} plan allows viewing up to {limits.MaxViewportAreaSqDegrees:0} square degrees. " +
                         "Zoom in or upgrade to see a larger area.",
                Vessels: Array.Empty<Vessel>());
        }

        await ReplaceGroupsAsync(limits.Cadence, bounds);

        var snapshot = _store.SnapshotInBounds(bounds);
        return new ViewportResult(Accepted: true, Message: null, Vessels: snapshot);
    }

    private async Task ReplaceGroupsAsync(RefreshCadence cadence, Bounds bounds)
    {
        var desired = Tiles.TilesCovering(bounds)
            .Select(t => Tiles.GroupName(cadence, t.Lat, t.Lon))
            .ToHashSet();

        var current = ConnectionGroups.GetOrAdd(Context.ConnectionId, _ => new HashSet<string>());
        var toLeave = current.Except(desired).ToList();
        var toJoin = desired.Except(current).ToList();

        foreach (var group in toLeave)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group);
        }

        foreach (var group in toJoin)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        }

        ConnectionGroups[Context.ConnectionId] = desired;
    }

    [Authorize]
    public async Task<IReadOnlyList<long>> FollowVessel(long mmsi)
    {
        var userId = Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var tier = TokenService.TierOf(Context.User);
        var limits = TierLimits.For(tier);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var current = await db.FollowedVessels.Where(f => f.UserId == userId).ToListAsync();
        if (current.All(f => f.Mmsi != mmsi))
        {
            if (current.Count >= limits.MaxFollowedVessels)
            {
                throw new HubException(
                    $"Your {tier} plan can follow up to {limits.MaxFollowedVessels} vessels.");
            }

            db.FollowedVessels.Add(new FollowedVessel { UserId = userId, Mmsi = mmsi });
            await db.SaveChangesAsync();
            current.Add(new FollowedVessel { UserId = userId, Mmsi = mmsi });
        }

        return current.Select(f => f.Mmsi).ToList();
    }

    [Authorize]
    public async Task<IReadOnlyList<long>> UnfollowVessel(long mmsi)
    {
        var userId = Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!;
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.FollowedVessels.Where(f => f.UserId == userId && f.Mmsi == mmsi).ExecuteDeleteAsync();
        return await db.FollowedVessels.Where(f => f.UserId == userId).Select(f => f.Mmsi).ToListAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        ConnectionGroups.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }
}
