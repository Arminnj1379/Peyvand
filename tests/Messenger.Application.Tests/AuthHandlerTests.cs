using Messenger.Application.Auth;
using Messenger.Application.Features.Auth;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

public sealed class AuthHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Ctx
    {
        public FakeUserStore Users = new();
        public FakeSessionStore Sessions = new();
        public FakePasswordHasher Passwords = new();
        public FakeClock Clock = new(Now);
        public FakeIds Ids = new();
        public FakeTokenService Tokens;
        public Ctx() => Tokens = new FakeTokenService(Now);
    }

    private static async Task<AuthResultDto> RegisterAsync(Ctx ctx, string username = "sara.m")
    {
        var handler = new RegisterCustomerHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        var result = await handler.Handle(
            new RegisterCustomerCommand(username, "Sara", "password123", null, "web", null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task Register_returns_tokens_and_customer_role()
    {
        var ctx = new Ctx();
        var auth = await RegisterAsync(ctx);

        Assert.StartsWith("access-", auth.AccessToken);
        Assert.NotEmpty(auth.RefreshToken);
        Assert.Contains("Customer", auth.User.Roles);
        Assert.Equal("sara.m", auth.User.Username);
    }

    [Fact]
    public async Task Register_duplicate_username_fails()
    {
        var ctx = new Ctx();
        await RegisterAsync(ctx);

        var handler = new RegisterCustomerHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        var result = await handler.Handle(
            new RegisterCustomerCommand("Sara.M", "Other", "password123", null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.UsernameTaken, result.Error);
    }

    [Fact]
    public async Task Register_lost_uniqueness_race_returns_taken()
    {
        var ctx = new Ctx();
        // Pre-checks pass (name is free) but the insert collides: a concurrent
        // registration won. Surfaces as 409, not an infrastructure exception.
        ctx.Users.ThrowDuplicateOnAdd = true;

        var handler = new RegisterCustomerHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        var result = await handler.Handle(
            new RegisterCustomerCommand("race.u", "Race", "password123", null, null, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.UsernameTaken, result.Error);
    }

    [Fact]
    public async Task Register_duplicate_phone_fails()
    {
        var ctx = new Ctx();
        var handler = new RegisterCustomerHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        var first = await handler.Handle(
            new RegisterCustomerCommand("u.one", "One", "password123", "+989121234567", null, null),
            CancellationToken.None);
        Assert.True(first.IsSuccess);

        var second = await handler.Handle(
            new RegisterCustomerCommand("u.two", "Two", "password123", "+989121234567", null, null),
            CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(UserErrors.PhoneTaken, second.Error);
    }

    [Fact]
    public async Task Login_succeeds_with_username_or_phone()
    {
        var ctx = new Ctx();
        var handler = new RegisterCustomerHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        await handler.Handle(
            new RegisterCustomerCommand("sara.m", "Sara", "password123", "+989121234567", null, null),
            CancellationToken.None);

        var login = new LoginHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        var byName = await login.Handle(new LoginCommand("SARA.M", "password123", null, null), CancellationToken.None);
        var byPhone = await login.Handle(new LoginCommand("+989121234567", "password123", null, null), CancellationToken.None);

        Assert.True(byName.IsSuccess);
        Assert.True(byPhone.IsSuccess);
    }

    [Fact]
    public async Task Login_failures_do_not_reveal_existence()
    {
        var ctx = new Ctx();
        await RegisterAsync(ctx);

        var login = new LoginHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        var unknown = await login.Handle(new LoginCommand("nobody", "password123", null, null), CancellationToken.None);
        var wrong = await login.Handle(new LoginCommand("sara.m", "wrong-pass", null, null), CancellationToken.None);

        Assert.True(unknown.IsFailure);
        Assert.True(wrong.IsFailure);
        Assert.Equal(unknown.Error, wrong.Error);
        Assert.Equal(AuthErrors.InvalidCredentials, unknown.Error);
    }

    [Fact]
    public async Task Login_inactive_account_fails()
    {
        var ctx = new Ctx();
        var auth = await RegisterAsync(ctx);
        var stored = await ctx.Users.GetByIdAsync(auth.User.Id, CancellationToken.None);
        stored!.Deactivate(Now);
        await ctx.Users.UpdateAsync(stored, CancellationToken.None);

        var login = new LoginHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        var result = await login.Handle(new LoginCommand("sara.m", "password123", null, null), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.AccountInactive, result.Error);
    }

    [Fact]
    public async Task Refresh_rotates_and_retires_old_token()
    {
        var ctx = new Ctx();
        var auth = await RegisterAsync(ctx);

        var refresh = new RefreshSessionHandler(ctx.Users, ctx.Sessions, ctx.Tokens, ctx.Clock);
        var rotated = await refresh.Handle(new RefreshSessionCommand(auth.RefreshToken, null), CancellationToken.None);

        Assert.True(rotated.IsSuccess);
        Assert.NotEqual(auth.RefreshToken, rotated.Value!.RefreshToken);

        // The stored session now points at the new hash.
        var session = await ctx.Sessions.GetByIdAsync(auth.SessionId, CancellationToken.None);
        Assert.Equal(Auth.RefreshTokens.Hash(rotated.Value.RefreshToken), session!.RefreshTokenHash);
        Assert.True(session.IsRetired(Auth.RefreshTokens.Hash(auth.RefreshToken)));
    }

    [Fact]
    public async Task Refresh_reuse_detected_revokes_everything()
    {
        var ctx = new Ctx();
        var auth = await RegisterAsync(ctx);

        var refresh = new RefreshSessionHandler(ctx.Users, ctx.Sessions, ctx.Tokens, ctx.Clock);
        var rotated = await refresh.Handle(new RefreshSessionCommand(auth.RefreshToken, null), CancellationToken.None);
        Assert.True(rotated.IsSuccess);

        // Attacker replays the stolen (rotated-out) token.
        var replay = await refresh.Handle(new RefreshSessionCommand(auth.RefreshToken, null), CancellationToken.None);
        Assert.True(replay.IsFailure);
        Assert.Equal(AuthErrors.ReuseDetected, replay.Error);

        // Even the legitimate latest token is now dead.
        var latest = await refresh.Handle(
            new RefreshSessionCommand(rotated.Value!.RefreshToken, null), CancellationToken.None);
        Assert.True(latest.IsFailure);
    }

    [Fact]
    public async Task Refresh_lost_rotation_race_fails_without_revoke()
    {
        var ctx = new Ctx();
        var auth = await RegisterAsync(ctx);

        // A concurrent rotation committed first: our compare-and-swap loses.
        // Contention is not theft — no sessions are revoked.
        ctx.Sessions.FailRotate = true;
        var refresh = new RefreshSessionHandler(ctx.Users, ctx.Sessions, ctx.Tokens, ctx.Clock);
        var lost = await refresh.Handle(new RefreshSessionCommand(auth.RefreshToken, null), CancellationToken.None);

        Assert.True(lost.IsFailure);
        Assert.Equal(AuthErrors.InvalidRefreshToken, lost.Error);
        var session = await ctx.Sessions.GetByIdAsync(auth.SessionId, CancellationToken.None);
        Assert.NotNull(session);
        Assert.False(session!.IsRevoked);
    }

    [Fact]
    public async Task Refresh_unknown_token_fails()
    {
        var ctx = new Ctx();
        var refresh = new RefreshSessionHandler(ctx.Users, ctx.Sessions, ctx.Tokens, ctx.Clock);
        var result = await refresh.Handle(new RefreshSessionCommand("bogus", null), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.InvalidRefreshToken, result.Error);
    }

    [Fact]
    public async Task ChangePassword_revokes_other_sessions_only()
    {
        var ctx = new Ctx();
        var auth = await RegisterAsync(ctx);
        var login = new LoginHandler(ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Tokens, ctx.Ids, ctx.Clock);
        var second = await login.Handle(new LoginCommand("sara.m", "password123", "mobile", null), CancellationToken.None);
        Assert.True(second.IsSuccess);

        var changer = new ChangePasswordHandler(
            ctx.Users, ctx.Sessions, ctx.Passwords, ctx.Clock,
            new FakeCurrentUser(auth.User.Id, sessionId: auth.SessionId));
        var changed = await changer.Handle(new ChangePasswordCommand("password123", "new-password-1"), CancellationToken.None);
        Assert.True(changed.IsSuccess);

        var sessions = await ctx.Sessions.ListByUserAsync(auth.User.Id, 100, CancellationToken.None);
        Assert.Contains(sessions, s => s.Id == auth.SessionId && !s.IsRevoked);
        Assert.Contains(sessions, s => s.Id == second.Value!.SessionId && s.IsRevoked);

        // Old password dead, new password works.
        var retry = await login.Handle(new LoginCommand("sara.m", "password123", null, null), CancellationToken.None);
        Assert.True(retry.IsFailure);
        var fresh = await login.Handle(new LoginCommand("sara.m", "new-password-1", null, null), CancellationToken.None);
        Assert.True(fresh.IsSuccess);
    }
}
