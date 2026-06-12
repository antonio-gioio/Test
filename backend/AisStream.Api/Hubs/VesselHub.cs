using Microsoft.AspNetCore.SignalR;

namespace AisStream.Api.Hubs;

/// <summary>
/// SignalR hub used to push live vessel updates to connected browsers.
/// The server broadcasts "VesselUpdated" events; clients do not call methods on it.
/// </summary>
public class VesselHub : Hub
{
}
