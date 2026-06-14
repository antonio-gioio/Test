namespace AisStream.Api.Messaging;

/// <summary>
/// Which responsibilities this process runs. Splitting roles is what lets the system scale
/// horizontally: exactly one Ingestor holds the single aisstream.io connection and writes
/// to the database, while many stateless Web nodes serve REST/SignalR behind a load
/// balancer. They are connected by the Redis vessel bus.
/// </summary>
public enum NodeRole
{
    /// <summary>Everything in one process (default; ideal for dev and small single-node deploys).</summary>
    All = 0,

    /// <summary>Only ingests AIS, persists to the database, and publishes updates to the bus.</summary>
    Ingestor = 1,

    /// <summary>Only serves clients: consumes the bus, fans out over SignalR, answers REST.</summary>
    Web = 2,
}

public class ClusterOptions
{
    public const string SectionName = "Cluster";

    public NodeRole Role { get; set; } = NodeRole.All;

    public bool RunsIngestion => Role is NodeRole.All or NodeRole.Ingestor;
    public bool RunsRealtime => Role is NodeRole.All or NodeRole.Web;
}

public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>StackExchange.Redis connection string. Empty = no Redis (single-node in-process bus).</summary>
    public string ConnectionString { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(ConnectionString);
}
