using MediatR;
using Messenger.Application.Auth;
using Messenger.Application.Features.Auth;

namespace Messenger.Api.Endpoints;

public sealed record RegisterRequest(
    string Username, string DisplayName, string Password, string? Phone, string? DeviceLabel);

public sealed record LoginRequest(string Login, string Password, string? DeviceLabel);

public sealed record RefreshRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Registration, login, refresh, logout, sessions, password change.</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest req, ISender sender, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await sender.Send(new RegisterCustomerCommand(
                req.Username, req.DisplayName, req.Password, req.Phone,
                req.DeviceLabel, ClientIp(ctx)), ct).ConfigureAwait(false);
            return ResultMapper.ToHttp(result, 201);
        })
        .WithName("RegisterCustomer")
        .WithSummary("Customer self-registration (also signs in).")
        .RequireRateLimiting("auth")
        .Produces<AuthResultDto>(201)
        .ProducesProblem(400)
        .ProducesProblem(409);

        group.MapPost("/login", async (LoginRequest req, ISender sender, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await sender.Send(new LoginCommand(
                req.Login, req.Password, req.DeviceLabel, ClientIp(ctx)), ct).ConfigureAwait(false);
            return ResultMapper.ToHttp(result);
        })
        .WithName("Login")
        .WithSummary("Username-or-phone login.")
        .RequireRateLimiting("auth")
        .Produces<AuthResultDto>()
        .ProducesProblem(400)
        .ProducesProblem(401);

        group.MapPost("/refresh", async (RefreshRequest req, ISender sender, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await sender.Send(new RefreshSessionCommand(req.RefreshToken, ClientIp(ctx)), ct)
                .ConfigureAwait(false);
            return ResultMapper.ToHttp(result);
        })
        .WithName("RefreshSession")
        .WithSummary("Rotates the refresh token (single use).")
        .RequireRateLimiting("auth")
        .Produces<AuthResultDto>()
        .ProducesProblem(400)
        .ProducesProblem(401);

        group.MapPost("/logout", async (ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new LogoutCommand(), ct).ConfigureAwait(false)))
        .RequireAuthorization()
        .WithName("Logout")
        .WithSummary("Revokes the current session.")
        .Produces(200)
        .ProducesProblem(401);

        group.MapPost("/logout-all", async (ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new LogoutAllCommand(), ct).ConfigureAwait(false)))
        .RequireAuthorization()
        .WithName("LogoutAll")
        .WithSummary("Revokes all sessions of the caller.")
        .Produces(200)
        .ProducesProblem(401);

        group.MapGet("/sessions", async (ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new ListSessionsQuery(), ct).ConfigureAwait(false)))
        .RequireAuthorization()
        .WithName("ListSessions")
        .WithSummary("Lists the caller's sessions.")
        .Produces<IReadOnlyList<SessionDto>>()
        .ProducesProblem(401);

        group.MapDelete("/sessions/{id}", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new RevokeSessionCommand(id), ct).ConfigureAwait(false)))
        .RequireAuthorization()
        .WithName("RevokeSession")
        .WithSummary("Revokes one of the caller's sessions.")
        .Produces(200)
        .ProducesProblem(401)
        .ProducesProblem(404);

        group.MapPost("/change-password", async (ChangePasswordRequest req, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ChangePasswordCommand(req.CurrentPassword, req.NewPassword), ct)
                .ConfigureAwait(false);
            return ResultMapper.ToHttp(result);
        })
        .RequireAuthorization()
        .WithName("ChangePassword")
        .WithSummary("Changes the password; other sessions are revoked.")
        .Produces(200)
        .ProducesProblem(400)
        .ProducesProblem(401);

        return app;
    }

    /// <summary>Server-observed IP for session records. Never taken from client input.</summary>
    private static string? ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString();
}
