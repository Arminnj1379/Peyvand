using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Domain.Tests;

public sealed class UserTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static Result<User> RegisterCustomer(string username = "sara.m", string? phone = null) =>
        User.Register("u1", username, "Sara", "HASH", UserType.Customer, [], phone, Now);

    [Fact]
    public void Register_customer_gets_base_role_and_event()
    {
        var result = RegisterCustomer();

        Assert.True(result.IsSuccess);
        var user = result.Value!;
        Assert.Equal("sara.m", user.Username);
        Assert.Contains(Role.Customer, user.Roles);
        Assert.True(user.IsActive);
        var registered = Assert.Single(user.DomainEvents);
        Assert.IsType<UserRegistered>(registered);
    }

    [Fact]
    public void Register_normalizes_username_to_lowercase()
    {
        var result = RegisterCustomer("Sara.M");
        Assert.True(result.IsSuccess);
        Assert.Equal("sara.m", result.Value!.Username);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("bad!char")]
    [InlineData("")]
    public void Register_rejects_bad_usernames(string username)
    {
        var result = RegisterCustomer(username);
        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidUsername, result.Error);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+98")]
    [InlineData("not-a-phone")]
    public void Register_rejects_bad_phones(string phone)
    {
        var result = RegisterCustomer(phone: phone);
        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.InvalidPhone, result.Error);
    }

    [Fact]
    public void Register_accepts_valid_phone()
    {
        var result = RegisterCustomer(phone: "+989121234567");
        Assert.True(result.IsSuccess);
        Assert.Equal("+989121234567", result.Value!.Phone);
    }

    [Fact]
    public void Base_role_cannot_be_removed()
    {
        var user = RegisterCustomer().Value!;
        var removed = user.RemoveRole(Role.Customer, Now);
        Assert.True(removed.IsFailure);
        Assert.Equal(UserErrors.BaseRoleRequired, removed.Error);
        Assert.Contains(Role.Customer, user.Roles);
    }

    [Fact]
    public void Non_base_role_can_be_added_and_removed()
    {
        var user = RegisterCustomer().Value!;
        Assert.True(user.AddRole(Role.SupportAgent, Now).IsSuccess);
        Assert.Contains(Role.SupportAgent, user.Roles);
        Assert.True(user.RemoveRole(Role.SupportAgent, Now).IsSuccess);
        Assert.DoesNotContain(Role.SupportAgent, user.Roles);
    }

    [Fact]
    public void Employee_cannot_take_customer_base_role()
    {
        var result = User.Register("u1", "reza", "Reza", "HASH", UserType.Employee, [Role.Customer], null, Now);
        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.BaseRoleRequired, result.Error);
    }

    [Fact]
    public void Deactivate_and_activate_flip_state()
    {
        var user = RegisterCustomer().Value!;
        user.Deactivate(Now);
        Assert.False(user.IsActive);
        user.Activate(Now);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void Block_self_fails_but_blocking_others_works()
    {
        var user = RegisterCustomer().Value!;
        var self = user.Block("u1", Now);
        Assert.True(self.IsFailure);
        Assert.Equal(UserErrors.CannotBlockSelf, self.Error);

        Assert.True(user.Block("u2", Now).IsSuccess);
        Assert.Contains("u2", user.BlockedUserIds);
        user.Unblock("u2", Now);
        Assert.DoesNotContain("u2", user.BlockedUserIds);
    }

    [Fact]
    public void UpdateProfile_rejects_long_bio()
    {
        var user = RegisterCustomer().Value!;
        var result = user.UpdateProfile("Sara", new string('x', 256), Now);
        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.BioTooLong, result.Error);
    }

    [Fact]
    public void UpdateSettings_accepts_known_themes_only()
    {
        var user = RegisterCustomer().Value!;
        Assert.Equal("system", user.Theme);
        Assert.True(user.NotificationsEnabled);

        Assert.True(user.UpdateSettings("Dark", false, Now).IsSuccess);
        Assert.Equal("dark", user.Theme);
        Assert.False(user.NotificationsEnabled);

        var bad = user.UpdateSettings("neon", true, Now);
        Assert.True(bad.IsFailure);
        Assert.Equal(UserErrors.InvalidTheme, bad.Error);
        Assert.Equal("dark", user.Theme);
    }

    [Fact]
    public void Rehydrate_round_trips_stored_state_without_events()
    {
        var user = RegisterCustomer(phone: "+989121234567").Value!;
        user.AddRole(Role.SupportAgent, Now);
        user.Block("u9", Now);

        var snapshot = new UserSnapshot(
            user.Id, user.Username, user.Phone, user.PhoneVerified, user.UserType,
            user.Roles.ToList(), user.DisplayName, user.Bio, user.Theme, user.NotificationsEnabled,
            user.AvatarObjectKey, user.AvatarUrl,
            user.PasswordHash, user.IsActive, user.BlockedUserIds.ToList(),
            user.LastSeenAt, user.CreatedAt, user.UpdatedAt);

        var revived = User.Rehydrate(snapshot);
        Assert.Equal(user.Id, revived.Id);
        Assert.Equal(user.Username, revived.Username);
        Assert.Equal(user.Roles.OrderBy(r => r), revived.Roles.OrderBy(r => r));
        Assert.Equal(user.BlockedUserIds, revived.BlockedUserIds);
        Assert.Empty(revived.DomainEvents);
    }
}
