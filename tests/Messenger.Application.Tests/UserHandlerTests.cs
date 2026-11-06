using Messenger.Application.Auth;
using Messenger.Application.Features.Users;
using Messenger.Application.Services;
using Messenger.Application.Services.Implementations;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

public sealed class UserHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x01, 0x02, 0x03
    ];

    private sealed class Ctx
    {
        public FakeUserStore Users = new();
        public FakeClock Clock = new(Now);
        public FakeIds Ids = new();
        public FakeAvatarStorage Avatars = new();
        public FakeChatStore Chats = new();
    }

    private static async Task<User> SeedCustomerAsync(Ctx ctx, string username = "sara.m")
    {
        var registered = User.Register(
            ctx.Ids.NewId(), username, "Sara", "H:pw", UserType.Customer, [], null, Now);
        Assert.True(registered.IsSuccess);
        await ctx.Users.AddAsync(registered.Value!, CancellationToken.None);
        return registered.Value!;
    }

    private static async Task<User> SeedEmployeeAsync(Ctx ctx, string username = "reza.e")
    {
        var registered = User.Register(
            ctx.Ids.NewId(), username, "Reza", "H:pw", UserType.Employee, [], null, Now);
        Assert.True(registered.IsSuccess);
        await ctx.Users.AddAsync(registered.Value!, CancellationToken.None);
        return registered.Value!;
    }

    [Fact]
    public async Task Search_hides_customers_from_customers()
    {
        var ctx = new Ctx();
        var customer = await SeedCustomerAsync(ctx);
        await SeedCustomerAsync(ctx, "sara.other");
        await SeedEmployeeAsync(ctx, "sara.employee");

        var handler = new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock);
        var result = await handler.SearchUsersAsync(
            new SearchUsersQuery("sara", 10), new CallerContext(customer.Id, null, []), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(result.Value!, u => u.UserType == "Customer" && u.Id != customer.Id);
        Assert.Contains(result.Value!, u => u.Username == "sara.employee");
    }

    [Fact]
    public async Task Customer_cannot_view_another_customer_profile()
    {
        var ctx = new Ctx();
        var customer = await SeedCustomerAsync(ctx);
        var other = await SeedCustomerAsync(ctx, "other.c");

        var handler = new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock);
        var result = await handler.GetUserByIdAsync(
            new GetUserByIdQuery(other.Id), new CallerContext(customer.Id, null, []), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Customers_sharing_a_dm_can_view_profiles_and_avatars()
    {
        var ctx = new Ctx();
        var customer = await SeedCustomerAsync(ctx);
        var other = await SeedCustomerAsync(ctx, "other.c");

        var dm = Messenger.Domain.Chats.Chat.CreateDm(
            ctx.Ids.NewId(), customer.Id, other.Id, Now).Value!;
        ctx.Chats.Seed(dm);

        var profile = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .GetUserByIdAsync(new GetUserByIdQuery(other.Id), new CallerContext(customer.Id, null, []), CancellationToken.None);
        Assert.True(profile.IsSuccess);
        Assert.Equal(other.Id, profile.Value!.Id);

        var uploaded = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .UploadAvatarAsync(new UploadAvatarCommand(PngBytes, "image/png"), new CallerContext(other.Id, null, []), CancellationToken.None);
        Assert.True(uploaded.IsSuccess);

        var fetched = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .GetAvatarAsync(new GetAvatarQuery(other.Id), new CallerContext(customer.Id, null, []), CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(PngBytes, fetched.Value!.Bytes);
    }

    [Fact]
    public async Task Customers_without_shared_chat_cannot_fetch_avatar()
    {
        var ctx = new Ctx();
        var customer = await SeedCustomerAsync(ctx);
        var other = await SeedCustomerAsync(ctx, "other.c");

        var uploaded = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .UploadAvatarAsync(new UploadAvatarCommand(PngBytes, "image/png"), new CallerContext(other.Id, null, []), CancellationToken.None);
        Assert.True(uploaded.IsSuccess);

        var fetched = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .GetAvatarAsync(new GetAvatarQuery(other.Id), new CallerContext(customer.Id, null, []), CancellationToken.None);
        Assert.True(fetched.IsFailure);
        Assert.Equal(UserErrors.NotFound, fetched.Error);
    }

    [Fact]
    public async Task Block_self_fails()
    {
        var ctx = new Ctx();
        var customer = await SeedCustomerAsync(ctx);

        var handler = new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock);
        var result = await handler.BlockUserAsync(
            new BlockUserCommand(customer.Id), new CallerContext(customer.Id, null, []), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.CannotBlockSelf, result.Error);
    }

    [Fact]
    public async Task Avatar_upload_rejects_forged_content_type()
    {
        var ctx = new Ctx();
        var customer = await SeedCustomerAsync(ctx);

        var handler = new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock);
        var forged = await handler.UploadAvatarAsync(
            new UploadAvatarCommand("not-an-image-at-all"u8.ToArray(), "image/png"), new CallerContext(customer.Id, null, []), CancellationToken.None);

        Assert.True(forged.IsFailure);
        Assert.Equal(UserErrors.AvatarUnsupportedType, forged.Error);
    }

    [Fact]
    public async Task Avatar_upload_and_remove_round_trip()
    {
        var ctx = new Ctx();
        var customer = await SeedCustomerAsync(ctx);
        var me = new CallerContext(customer.Id, null, []);

        var uploaded = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .UploadAvatarAsync(new UploadAvatarCommand(PngBytes, "image/png"), me, CancellationToken.None);
        Assert.True(uploaded.IsSuccess);
        Assert.NotNull(uploaded.Value!.AvatarUrl);

        var fetched = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .GetAvatarAsync(new GetAvatarQuery(customer.Id), me, CancellationToken.None);
        Assert.True(fetched.IsSuccess);
        Assert.Equal(PngBytes, fetched.Value!.Bytes);
        Assert.Equal("image/png", fetched.Value.ContentType);

        var removed = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .RemoveAvatarAsync(me, CancellationToken.None);
        Assert.True(removed.IsSuccess);
        Assert.Null(removed.Value!.AvatarUrl);

        var gone = await new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock)
            .GetAvatarAsync(new GetAvatarQuery(customer.Id), me, CancellationToken.None);
        Assert.True(gone.IsFailure);
    }

    [Fact]
    public void UploadAvatar_validator_rejects_oversize_and_bad_type()
    {
        var validator = new UploadAvatarValidator();

        var badType = validator.Validate(new UploadAvatarCommand(new byte[10], "image/gif"));
        Assert.False(badType.IsValid);
        Assert.Contains(badType.Errors, e => e.ErrorMessage.Contains("JPEG"));

        var tooBig = validator.Validate(
            new UploadAvatarCommand(new byte[Features.Users.UploadAvatarCommand.MaxBytes + 1], "image/png"));
        Assert.False(tooBig.IsValid);
        Assert.Contains(tooBig.Errors, e => e.ErrorMessage.Contains("2 MB"));
    }
}
