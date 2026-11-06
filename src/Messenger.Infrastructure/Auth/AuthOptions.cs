using System.ComponentModel.DataAnnotations;

namespace Messenger.Infrastructure.Auth;

/// <summary>JWT settings. Bound from the <c>Jwt</c> config section, validated on startup.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "messenger";

    [Required]
    public string Audience { get; set; } = "messenger";

    [Required, MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int AccessTokenMinutes { get; set; } = 15;
}

/// <summary>Bootstrap admin account. Disabled unless explicitly enabled in config.</summary>
public sealed class SeedAdminOptions
{
    public const string SectionName = "SeedAdmin";

    public bool Enabled { get; set; }
    public string Username { get; set; } = "admin";
    public string DisplayName { get; set; } = "Administrator";
    public string Password { get; set; } = string.Empty;
}
