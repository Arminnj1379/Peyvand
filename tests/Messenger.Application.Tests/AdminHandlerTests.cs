using Messenger.Application.Auth;
using Messenger.Application.Features.Admin;
using Messenger.Application.Features.Auth;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

public sealed class AdminHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Ctx
    {
        public FakeUserStore Users = new();
        public FakeSessionStore Sessions = new();
        public FakePasswordHasher Passwords = new();
        public FakeClock Clock = new(Now);
        public FakeIds Ids = new();
    }

    private static async Task<User> SeedAdminAsync(Ctx ctx)
    {
        var registered = User.Register(
            ctx.Ids.NewId(), "root", "Root", ctx.Passwords.Hash("password123"),
            UserType.Employee, [Role.SystemAdministrator], null, Now);
        Assert.True(registered.IsSuccess);
        await ctx.Users.AddAsync(registered.Value!, CancellationToken.None);
        return registered.Value!;
    }

    private static FakeCurrentUser As(User user, string? sessionId = null) =>
        new(user.Id, user.Roles.Select(r => r.ToString()).ToList(), sessionId, user.Username);

    [Fact]
    public async Task Customer_cannot_create_employees()
    {
        var ctx = new Ctx();
        var registered = User.Register(
            ctx.Ids.NewId(), "cust", "Cust", ctx.Passwords.Hash("password123"),
            UserType.Customer, [], null, Now);
        await ctx.Users.AddAsync(registered.Value!, CancellationToken.None);

        var handler = new CreateEmployeeHandler(ctx.Users, ctx.Passwords, ctx.Ids, ctx.Clock, As(registered.Value!));
        var result = await handler.Handle(
            new CreateEmployeeCommand("emp1", "Emp", "password123", ["Employee"], null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(AuthErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task OrgAdmin_cannot_grant_admin_roles_but_sysadmin_can()
    {
        var ctx = new Ctx();
        var org = User.Register(
            ctx.Ids.NewId(), "org", "Org", ctx.Passwords.Hash("password123"),
            UserType.Employee, [Role.OrganizationAdministrator], null, Now);
        await ctx.Users.AddAsync(org.Value!, CancellationToken.None);
        var sysadmin = await SeedAdminAsync(ctx);

        var denied = await new CreateEmployeeHandler(ctx.Users, ctx.Passwords, ctx.Ids, ctx.Clock, As(org.Value!))
            .Handle(new CreateEmployeeCommand("emp.one", "E", "password123", ["SupportAgent", "SystemAdministrator"], null),
                CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(AuthErrors.CannotGrantRole, denied.Error);

        var allowed = await new CreateEmployeeHandler(ctx.Users, ctx.Passwords, ctx.Ids, ctx.Clock, As(sysadmin))
            .Handle(new CreateEmployeeCommand("emp.one", "E", "password123", ["SupportAgent", "OrganizationAdministrator"], null),
                CancellationToken.None);
        Assert.True(allowed.IsSuccess);
        Assert.Contains("SupportAgent", allowed.Value!.Roles);
        Assert.Contains("OrganizationAdministrator", allowed.Value.Roles);
        Assert.Contains("Employee", allowed.Value.Roles);
    }

    [Fact]
    public async Task Deactivation_revokes_sessions_and_blocks_login()
    {
        var ctx = new Ctx();
        var sysadmin = await SeedAdminAsync(ctx);

        var created = await new CreateEmployeeHandler(ctx.Users, ctx.Passwords, ctx.Ids, ctx.Clock, As(sysadmin))
            .Handle(new CreateEmployeeCommand("emp1", "Emp", "password123", [], null), CancellationToken.None);
        Assert.True(created.IsSuccess);

        // Employee signs in (session exists), then gets deactivated.
        var tokens = new FakeTokenService(Now);
        var login = new LoginHandler(ctx.Users, ctx.Sessions, ctx.Passwords, tokens, ctx.Ids, ctx.Clock);
        var signedIn = await login.Handle(new LoginCommand("emp1", "password123", null, null), CancellationToken.None);
        Assert.True(signedIn.IsSuccess);

        var deactivated = await new SetUserActiveHandler(ctx.Users, ctx.Sessions, ctx.Clock, As(sysadmin))
            .Handle(new SetUserActiveCommand(created.Value!.Id, false), CancellationToken.None);
        Assert.True(deactivated.IsSuccess);
        Assert.False(deactivated.Value!.IsActive);

        var sessions = await ctx.Sessions.ListByUserAsync(created.Value.Id, 100, CancellationToken.None);
        Assert.All(sessions, s => Assert.True(s.IsRevoked));

        var retry = await login.Handle(new LoginCommand("emp1", "password123", null, null), CancellationToken.None);
        Assert.True(retry.IsFailure);
        Assert.Equal(AuthErrors.AccountInactive, retry.Error);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_themselves()
    {
        var ctx = new Ctx();
        var sysadmin = await SeedAdminAsync(ctx);

        var result = await new SetUserActiveHandler(ctx.Users, ctx.Sessions, ctx.Clock, As(sysadmin))
            .Handle(new SetUserActiveCommand(sysadmin.Id, false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("user.cannot_change_self", result.Error.Code);
    }

    [Fact]
    public async Task SetRoles_replaces_non_base_roles()
    {
        var ctx = new Ctx();
        var sysadmin = await SeedAdminAsync(ctx);
        var created = await new CreateEmployeeHandler(ctx.Users, ctx.Passwords, ctx.Ids, ctx.Clock, As(sysadmin))
            .Handle(new CreateEmployeeCommand("agent1", "Agent", "password123", ["SupportAgent"], null),
                CancellationToken.None);
        Assert.True(created.IsSuccess);

        var updated = await new SetUserRolesHandler(ctx.Users, ctx.Clock, As(sysadmin))
            .Handle(new SetUserRolesCommand(created.Value!.Id, ["Employee"]), CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Contains("Employee", updated.Value!.Roles);
        Assert.DoesNotContain("SupportAgent", updated.Value.Roles);
    }
}
