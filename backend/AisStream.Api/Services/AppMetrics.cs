using Prometheus;

namespace AisStream.Api.Services;

/// <summary>Domain Prometheus metrics, exposed at /metrics alongside the HTTP request metrics.</summary>
public static class AppMetrics
{
    public static readonly Counter BusUpdates = Metrics.CreateCounter(
        "ais_bus_updates_total", "Vessel updates processed from the message bus.");

    public static readonly Gauge CachedVessels = Metrics.CreateGauge(
        "ais_vessels_cached", "Vessels currently held in the in-memory cache.");

    public static readonly Gauge SignalRConnections = Metrics.CreateGauge(
        "ais_signalr_connections", "Active SignalR hub connections on this node.");
}
