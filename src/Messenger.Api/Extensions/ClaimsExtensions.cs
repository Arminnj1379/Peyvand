using System.Security.Claims;
using Messenger.Application.Services;

namespace Messenger.Api.Extensions;

/// <summary>Builds the application-layer caller from validated JWT claims. No client input trusted.</summary>
internal static class ClaimsExtensions
{
    internal static CallerContext ToCaller(this ClaimsPrincipal user) => new(
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value,
        user.FindFirst("sid")?.Value,
        user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList());

    internal static string? ActorId(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? user.FindFirst("sub")?.Value
        ?? "unknown";

    internal static string? ClientIp(this HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();
}
