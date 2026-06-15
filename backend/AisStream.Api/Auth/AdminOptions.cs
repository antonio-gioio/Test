namespace AisStream.Api.Auth;

public static class Roles
{
    public const string Admin = "Admin";
}

/// <summary>Credentials for the admin account seeded on startup.</summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string Email { get; set; } = "admin@aisstream.local";

    /// <summary>Initial admin password. Override via Admin__Password; change after first login.</summary>
    public string Password { get; set; } = "Admin123!";
}
