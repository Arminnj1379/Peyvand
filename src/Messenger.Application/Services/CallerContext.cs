namespace Messenger.Application.Services;

/// <summary>
/// Identity of the current caller, built once per request from the validated
/// JWT (never from client input). Services take this explicitly so they stay
/// free of HTTP concerns and are trivial to unit test.
/// </summary>
public sealed record CallerContext(string? UserId, string? SessionId, IReadOnlyList<string> Roles)
{
    public static CallerContext Anonymous { get; } = new(null, null, []);

    public bool HasRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
}
