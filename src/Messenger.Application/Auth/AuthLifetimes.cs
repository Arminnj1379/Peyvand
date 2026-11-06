namespace Messenger.Application.Auth;

/// <summary>Token lifetimes (policy). The access-token TTL itself is owned by JwtOptions.</summary>
public static class AuthLifetimes
{
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
}
