using System.Security.Cryptography;

namespace Messenger.Application.Auth;

/// <summary>
/// Opaque refresh-token minting and hashing. Pure static helpers shared by
/// handlers, tests, and the session store lookup (hash only, never the token).
/// </summary>
public static class RefreshTokens
{
    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string Hash(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
