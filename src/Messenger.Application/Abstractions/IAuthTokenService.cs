using Messenger.Domain.Users;

namespace Messenger.Application.Abstractions;

/// <summary>Issues and validates the opaque refresh tokens (hashes only are stored).</summary>
public interface IAuthTokenService
{
    /// <summary>Creates a signed JWT access token for the user + session.</summary>
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user, string sessionId);

    /// <summary>Generates a fresh opaque refresh token (returned to the caller once).</summary>
    string CreateRefreshToken();
}
