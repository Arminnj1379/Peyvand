using System.Security.Claims;
using Messenger.Application.Abstractions;

namespace Messenger.Api.Auth;

/// <summary>Builds <see cref="ICurrentUser"/> from validated JWT claims. No client input trusted.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => Claim(ClaimTypes.NameIdentifier) ?? Claim("sub");
    public string? Username => Claim("username");
    public string? SessionId => Claim("sid");
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;

    public IReadOnlyList<string> Roles =>
        accessor.HttpContext?.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList()
        ?? [];

    private string? Claim(string type) => accessor.HttpContext?.User.FindFirst(type)?.Value;
}
