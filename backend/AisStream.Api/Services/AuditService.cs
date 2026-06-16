using System.Security.Claims;
using AisStream.Api.Data;

namespace AisStream.Api.Services;

/// <summary>Records privileged actions to the audit log. Scoped, shares the request DbContext.</summary>
public class AuditService
{
    private readonly AppDbContext _db;

    public AuditService(AppDbContext db) => _db = db;

    public async Task RecordAsync(ClaimsPrincipal actor, string action, string? detail = null)
    {
        var email = actor.FindFirstValue(ClaimTypes.Email)
            ?? actor.Identity?.Name
            ?? "unknown";

        _db.AuditLogs.Add(new AuditLog { Actor = email, Action = action, Detail = detail });
        await _db.SaveChangesAsync();
    }
}
