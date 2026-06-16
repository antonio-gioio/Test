namespace AisStream.Api.Data;

/// <summary>An immutable record of a privileged (admin) action, for accountability.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Detail { get; set; }
}
