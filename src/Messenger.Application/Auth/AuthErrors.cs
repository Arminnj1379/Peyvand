using Messenger.Domain.Common;

namespace Messenger.Application.Auth;

/// <summary>Authentication/session failures. Messages are safe to surface (no user enumeration).</summary>
public static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        new("auth.invalid_credentials", "Username or password is incorrect.");

    public static readonly Error AccountInactive =
        new("auth.account_inactive", "This account is deactivated.");

    public static readonly Error InvalidRefreshToken =
        new("auth.invalid_refresh_token", "Refresh token is invalid or expired.");

    public static readonly Error SessionRevoked =
        new("auth.session_revoked", "Session has been revoked. Please sign in again.");

    public static readonly Error ReuseDetected =
        new("auth.reuse_detected", "Refresh token reuse detected. All sessions were revoked for safety.");

    public static readonly Error Forbidden =
        new("auth.forbidden", "You do not have permission to perform this action.");

    public static readonly Error CannotGrantRole =
        new("auth.cannot_grant_role", "Only a System Administrator can grant this role.");

    public static readonly Error Unauthenticated =
        new("auth.unauthenticated", "Authentication is required.");
}
