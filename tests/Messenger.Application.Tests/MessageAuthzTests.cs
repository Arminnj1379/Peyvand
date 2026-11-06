using Messenger.Application.Features.Chats;
using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Messages;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

/// <summary>Authorization and ownership rules for the messaging feature set
/// (reply/forward/edit/delete/reactions/pins/selection). The handlers under
/// test already implement the full feature set; these tests lock the
/// permission boundaries: sender-only edits, own-or-admin deletes (global,
/// i.e. for-everyone), member-only reactions, admin-only group pins, and
/// atomic bulk deletes.</summary>
public sealed class MessageAuthzTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Ctx
    {
        public FakeUserStore Users = new();
        public FakeChatStore Chats = new();
        public FakeMessageStore Messages = new();
        public FakeFileStore Files = new();
        public FakeClock Clock = new(Now);
        public FakeIds Ids = new();

        public async Task<User> SeedUserAsync(string username)
        {
            var registered = User.Register(
                Ids.NewId(), username, username, "H:pw", UserType.Employee, [], null, Now);
            Assert.True(registered.IsSuccess);
            await Users.AddAsync(registered.Value!, CancellationToken.None);
            return registered.Value!;
        }

        public FakeCurrentUser As(User user) => new(user.Id);
    }

    private static async Task<Chat> SeedGroupAsync(Ctx ctx, User owner, params User[] members)
    {
        var ids = new List<string> { owner.Id };
        ids.AddRange(members.Select(m => m.Id));
        var handler = new CreateGroupHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(owner));
        var result = await handler.Handle(
            new CreateGroupCommand("Team", ids, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return (await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
    }

    private static async Task<Chat> SeedChannelAsync(Ctx ctx, User owner, params User[] subs)
    {
        var handler = new CreateChannelHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(owner));
        var result = await handler.Handle(
            new CreateChannelCommand("News", null, subs.Select(s => s.Id).ToList(), false, null),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return (await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
    }

    private static async Task<Chat> SeedDmAsync(Ctx ctx, User a, User b)
    {
        var handler = new CreateDmHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a));
        var result = await handler.Handle(new CreateDmCommand(b.Id), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return (await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
    }

    private static async Task<MessageDto> SendAsync(
        Ctx ctx, User sender, string chatId, string body, string clientId, string? replyTo = null)
    {
        var handler = new SendMessageHandler(
            ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(sender));
        var result = await handler.Handle(
            new SendMessageCommand(chatId, body, clientId, replyTo), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task Edit_deleted_message_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("e1.a");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("e1.b"));
        var sent = await SendAsync(ctx, a, chat.Id, "original", "k-1");

        var deleted = await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var edit = await new EditMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new EditMessageCommand(sent.Id, "resurrect"), CancellationToken.None);
        Assert.True(edit.IsFailure);
        // Deleted messages read back as not-found (no existence leak).
        Assert.Equal(MessageErrors.NotFound, edit.Error);
    }

    [Fact]
    public async Task Edit_empty_body_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("e2.a");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("e2.b"));
        var sent = await SendAsync(ctx, a, chat.Id, "original", "k-1");

        var edit = await new EditMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new EditMessageCommand(sent.Id, "   "), CancellationToken.None);
        Assert.True(edit.IsFailure);
        Assert.Equal(MessageErrors.InvalidBody, edit.Error);
    }

    [Fact]
    public async Task Edit_by_non_member_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("e3.a");
        var outsider = await ctx.SeedUserAsync("e3.out");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("e3.b"));
        var sent = await SendAsync(ctx, a, chat.Id, "original", "k-1");

        var edit = await new EditMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(outsider))
            .Handle(new EditMessageCommand(sent.Id, "hijack"), CancellationToken.None);
        Assert.True(edit.IsFailure);
        // Ownership is checked before membership, so a non-sender outsider
        // deterministically sees NotSender (never a membership leak).
        Assert.Equal(MessageErrors.NotSender, edit.Error);
    }

    [Fact]
    public async Task Reply_to_deleted_message_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("rp1.a");
        var b = await ctx.SeedUserAsync("rp1.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var sent = await SendAsync(ctx, a, chat.Id, "original", "k-1");

        var deleted = await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var reply = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(b))
            .Handle(new SendMessageCommand(chat.Id, "reply", "k-2", sent.Id), CancellationToken.None);
        Assert.True(reply.IsFailure);
        Assert.Equal(MessageErrors.ReplyTargetNotFound, reply.Error);
    }

    [Fact]
    public async Task Delete_already_deleted_returns_not_found()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("d1.a");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("d1.b"));
        var sent = await SendAsync(ctx, a, chat.Id, "bye", "k-1");

        var first = await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.True(first.Value!.IsDeleted);

        var second = await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);
        Assert.True(second.IsFailure);
        Assert.Equal(MessageErrors.NotFound, second.Error);
    }

    [Fact]
    public async Task Delete_by_outsider_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("d2.a");
        var outsider = await ctx.SeedUserAsync("d2.out");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("d2.b"));
        var sent = await SendAsync(ctx, a, chat.Id, "mine", "k-1");

        var result = await new DeleteMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(outsider))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(ChatErrors.Forbidden, result.Error);
    }

    [Fact]
    public async Task Reaction_on_deleted_message_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("r1.a");
        var b = await ctx.SeedUserAsync("r1.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var sent = await SendAsync(ctx, a, chat.Id, "hello", "k-1");

        var deleted = await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var react = await new ToggleReactionHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(b))
            .Handle(new ToggleReactionCommand(sent.Id, "👍"), CancellationToken.None);
        Assert.True(react.IsFailure);
        Assert.Equal(MessageErrors.NotFound, react.Error);
    }

    [Fact]
    public async Task Reaction_by_outsider_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("r2.a");
        var outsider = await ctx.SeedUserAsync("r2.out");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("r2.b"));
        var sent = await SendAsync(ctx, a, chat.Id, "hello", "k-1");

        var react = await new ToggleReactionHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(outsider))
            .Handle(new ToggleReactionCommand(sent.Id, "👍"), CancellationToken.None);
        Assert.True(react.IsFailure);
        Assert.Equal(ChatErrors.Forbidden, react.Error);
    }

    [Fact]
    public async Task Group_pin_requires_admin_member_pin_denied()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("p1.owner");
        var member = await ctx.SeedUserAsync("p1.member");
        var chat = await SeedGroupAsync(ctx, owner, member);
        var sent = await SendAsync(ctx, owner, chat.Id, "announce", "k-1");

        var denied = await new PinMessageHandler(ctx.Chats, ctx.Messages, ctx.Users, ctx.Clock, ctx.As(member))
            .Handle(new PinMessageCommand(chat.Id, sent.Id), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(ChatErrors.NotAdmin, denied.Error);

        var ok = await new PinMessageHandler(ctx.Chats, ctx.Messages, ctx.Users, ctx.Clock, ctx.As(owner))
            .Handle(new PinMessageCommand(chat.Id, sent.Id), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.Contains(sent.Id, ok.Value!.PinnedMessageIds);

        var unpinned = await new UnpinMessageHandler(ctx.Chats, ctx.Users, ctx.Clock, ctx.As(owner))
            .Handle(new UnpinMessageCommand(chat.Id, sent.Id), CancellationToken.None);
        Assert.True(unpinned.IsSuccess);
        Assert.DoesNotContain(sent.Id, unpinned.Value!.PinnedMessageIds);
    }

    [Fact]
    public async Task Dm_pin_by_member_succeeds_but_deleted_message_cannot_pin()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("p2.a");
        var b = await ctx.SeedUserAsync("p2.b");
        var chat = await SeedDmAsync(ctx, a, b);
        var sent = await SendAsync(ctx, a, chat.Id, "note", "k-1");

        // Either DM member may pin (no admin concept in DMs).
        var ok = await new PinMessageHandler(ctx.Chats, ctx.Messages, ctx.Users, ctx.Clock, ctx.As(b))
            .Handle(new PinMessageCommand(chat.Id, sent.Id), CancellationToken.None);
        Assert.True(ok.IsSuccess);

        await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);

        var other = await SendAsync(ctx, a, chat.Id, "other", "k-2");
        await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(other.Id), CancellationToken.None);

        var pinDeleted = await new PinMessageHandler(ctx.Chats, ctx.Messages, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new PinMessageCommand(chat.Id, other.Id), CancellationToken.None);
        Assert.True(pinDeleted.IsFailure);
        Assert.Equal(MessageErrors.NotFound, pinDeleted.Error);
    }

    [Fact]
    public async Task Forward_deleted_source_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("f1.a");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("f1.b"));
        var other = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("f1.c"));
        var sent = await SendAsync(ctx, a, chat.Id, "gone", "k-1");

        await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);

        var fwd = await new ForwardMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new ForwardMessageCommand(chat.Id, sent.Id, other.Id, "k-fwd"), CancellationToken.None);
        Assert.True(fwd.IsFailure);
        Assert.Equal(MessageErrors.ForwardSourceNotFound, fwd.Error);
    }

    [Fact]
    public async Task Forward_by_source_outsider_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("f2.a");
        var outsider = await ctx.SeedUserAsync("f2.out");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("f2.b"));
        var other = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("f2.c"));
        var sent = await SendAsync(ctx, a, chat.Id, "hello", "k-1");

        var fwd = await new ForwardMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(outsider))
            .Handle(new ForwardMessageCommand(chat.Id, sent.Id, other.Id, "k-fwd"), CancellationToken.None);
        Assert.True(fwd.IsFailure);
        Assert.Equal(ChatErrors.Forbidden, fwd.Error);
    }

    [Fact]
    public async Task Forward_to_readonly_channel_is_rejected()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("f3.owner");
        var sub = await ctx.SeedUserAsync("f3.sub");
        var group = await SeedGroupAsync(ctx, owner, sub);
        var channel = await SeedChannelAsync(ctx, owner, sub);
        var sent = await SendAsync(ctx, owner, group.Id, "hello", "k-1");

        // Subscribers cannot post, so they cannot forward into the channel either.
        var fwd = await new ForwardMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(sub))
            .Handle(new ForwardMessageCommand(group.Id, sent.Id, channel.Id, "k-fwd"), CancellationToken.None);
        Assert.True(fwd.IsFailure);
        Assert.Equal(ChatErrors.CannotPost, fwd.Error);
    }

    [Fact]
    public async Task Forward_retry_with_same_client_id_returns_original()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("f4.a");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("f4.b"));
        var other = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("f4.c"));
        var sent = await SendAsync(ctx, a, chat.Id, "hello", "k-1");

        var handler = new ForwardMessageHandler(
            ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a));
        var first = await handler.Handle(
            new ForwardMessageCommand(chat.Id, sent.Id, other.Id, "k-fwd"), CancellationToken.None);
        var second = await handler.Handle(
            new ForwardMessageCommand(chat.Id, sent.Id, other.Id, "k-fwd"), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(first.Value.Seq, second.Value!.Seq);
    }

    [Fact]
    public async Task BulkDelete_is_atomic_on_authorization_failure()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("bd.owner");
        var member = await ctx.SeedUserAsync("bd.member");
        var chat = await SeedGroupAsync(ctx, owner, member);
        var own = await SendAsync(ctx, member, chat.Id, "mine", "k-1");
        var foreign = await SendAsync(ctx, owner, chat.Id, "theirs", "k-2");

        // Member selects own + owner's message: the whole batch fails...
        var denied = await new BulkDeleteMessagesHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(member))
            .Handle(new BulkDeleteMessagesCommand([own.Id, foreign.Id]), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(MessageErrors.CannotDelete, denied.Error);

        // ...and nothing was deleted (no partial delete left behind).
        Assert.False((await ctx.Messages.GetByIdAsync(own.Id, CancellationToken.None))!.IsDeleted);
        Assert.False((await ctx.Messages.GetByIdAsync(foreign.Id, CancellationToken.None))!.IsDeleted);
    }
}
