namespace Messenger.Application.Abstractions;

/// <summary>
/// Current caller derived from the validated JWT.
/// Handlers must use this — never trust client-supplied user ids.
/// </summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? Username { get; }
    string? SessionId { get; }
    bool IsAuthenticated { get; }
    IReadOnlyList<string> Roles { get; }

    bool HasRole(string role) => Roles.Contains(role);
}
