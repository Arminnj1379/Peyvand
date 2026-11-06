using Messenger.Application.Features.Chats;
using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Messages;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

public sealed class MessagingHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Ctx
    {
        public FakeUserStore Users = new();
        public FakeChatStore Chats = new();
        public FakeMessageStore Messages = new();
        public FakeFileStore Files = new();
        public FakeFileStorage Storage = new();
        public FakeFileScanner Scanner = new();
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
        var chat = await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None);
        return chat!;
    }

    private static async Task<MessageDto> SendAsync(Ctx ctx, User sender, string chatId, string body, string clientId)
    {
        var handler = new SendMessageHandler(
            ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(sender));
        var result = await handler.Handle(
            new SendMessageCommand(chatId, body, clientId, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task CreateDm_is_idempotent_for_same_pair()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("dm.a");
        var b = await ctx.SeedUserAsync("dm.b");

        var handler = new CreateDmHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a));
        var first = await handler.Handle(new CreateDmCommand(b.Id), CancellationToken.None);
        var second = await handler.Handle(new CreateDmCommand(b.Id), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
    }

    [Fact]
    public async Task CreateDm_lost_insert_race_returns_winner()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("dmr.a");
        var b = await ctx.SeedUserAsync("dmr.b");
        ctx.Chats.SimulateDmRaceOnAdd = true;

        var handler = new CreateDmHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a));
        var result = await handler.Handle(new CreateDmCommand(b.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("race-winner", result.Value!.Id);
    }

    [Fact]
    public async Task Send_assigns_monotonic_seq_and_enriches_sender()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("seq.a");
        var b = await ctx.SeedUserAsync("seq.b");
        var chat = await SeedGroupAsync(ctx, a, b);

        var m1 = await SendAsync(ctx, a, chat.Id, "one", "k-1");
        var m2 = await SendAsync(ctx, b, chat.Id, "two", "k-2");

        Assert.Equal(1, m1.Seq);
        Assert.Equal(2, m2.Seq);
        Assert.Equal("seq.a", m1.SenderUsername);
        var stored = await ctx.Chats.GetByIdAsync(chat.Id, CancellationToken.None);
        Assert.Equal(2, stored!.SeqCounter);
        Assert.Equal("two", stored.LastMessagePreview);
    }

    [Fact]
    public async Task Send_is_idempotent_on_client_message_id()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("idem.a");
        var b = await ctx.SeedUserAsync("idem.b");
        var chat = await SeedGroupAsync(ctx, a, b);

        var first = await SendAsync(ctx, a, chat.Id, "hello", "retry-key");
        var second = await SendAsync(ctx, a, chat.Id, "hello", "retry-key");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Seq, second.Seq);
        var history = await new GetHistoryHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.As(a))
            .Handle(new GetHistoryQuery(chat.Id, null, 10), CancellationToken.None);
        Assert.True(history.IsSuccess);
        Assert.Single(history.Value!.Items);
    }

    [Fact]
    public async Task Non_member_cannot_send_or_read()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("authz.a");
        var b = await ctx.SeedUserAsync("authz.b");
        var outsider = await ctx.SeedUserAsync("authz.out");
        var chat = await SeedGroupAsync(ctx, a, b);

        var send = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(outsider))
            .Handle(new SendMessageCommand(chat.Id, "hi", "k-9", null), CancellationToken.None);
        Assert.True(send.IsFailure);
        Assert.Equal(ChatErrors.Forbidden, send.Error);

        var history = await new GetHistoryHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.As(outsider))
            .Handle(new GetHistoryQuery(chat.Id, null, 10), CancellationToken.None);
        Assert.True(history.IsFailure);
        Assert.Equal(ChatErrors.Forbidden, history.Error);
    }

    [Fact]
    public async Task Channel_subscriber_cannot_post()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("chan.owner");
        var sub = await ctx.SeedUserAsync("chan.sub");
        var created = await new CreateChannelHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(owner))
            .Handle(new CreateChannelCommand("News", null, [sub.Id]), CancellationToken.None);
        Assert.True(created.IsSuccess);

        var send = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(sub))
            .Handle(new SendMessageCommand(created.Value!.Id, "hi", "k-1", null), CancellationToken.None);
        Assert.True(send.IsFailure);
        Assert.Equal(ChatErrors.CannotPost, send.Error);
    }

    [Fact]
    public async Task History_paginates_by_seq_cursor()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("page.a");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("page.b"));
        for (var i = 1; i <= 5; i++)
        {
            await SendAsync(ctx, a, chat.Id, $"msg {i}", $"k-{i}");
        }

        var handler = new GetHistoryHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.As(a));
        var latest = await handler.Handle(new GetHistoryQuery(chat.Id, null, 2), CancellationToken.None);

        Assert.True(latest.IsSuccess);
        Assert.True(latest.Value!.HasMore);
        Assert.Equal([4, 5], latest.Value.Items.Select(m => m.Seq).ToList());
        Assert.NotNull(latest.Value.NextCursor);

        var older = await handler.Handle(
            new GetHistoryQuery(chat.Id, latest.Value.NextCursor, 2), CancellationToken.None);
        Assert.True(older.IsSuccess);
        Assert.Equal([2, 3], older.Value!.Items.Select(m => m.Seq).ToList());

        var oldest = await handler.Handle(
            new GetHistoryQuery(chat.Id, older.Value.NextCursor, 2), CancellationToken.None);
        Assert.True(oldest.IsSuccess);
        Assert.Equal([1], oldest.Value!.Items.Select(m => m.Seq).ToList());
        Assert.False(oldest.Value.HasMore);
    }

    [Fact]
    public async Task Sync_returns_only_messages_after_watermark()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("sync.a");
        var chat = await SeedGroupAsync(ctx, a, await ctx.SeedUserAsync("sync.b"));
        await SendAsync(ctx, a, chat.Id, "one", "k-1");
        await SendAsync(ctx, a, chat.Id, "two", "k-2");
        await SendAsync(ctx, a, chat.Id, "three", "k-3");

        var result = await new SyncMessagesHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.As(a))
            .Handle(new SyncMessagesQuery(chat.Id, 1, 10), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([2, 3], result.Value!.Select(m => m.Seq).ToList());
    }

    [Fact]
    public async Task Edit_allows_sender_only()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("edit.a");
        var b = await ctx.SeedUserAsync("edit.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var sent = await SendAsync(ctx, a, chat.Id, "original", "k-1");

        var edited = await new EditMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new EditMessageCommand(sent.Id, "updated"), CancellationToken.None);
        Assert.True(edited.IsSuccess);
        Assert.True(edited.Value!.IsEdited);
        Assert.Equal("updated", edited.Value.Body);

        var theft = await new EditMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(b))
            .Handle(new EditMessageCommand(sent.Id, "hijack"), CancellationToken.None);
        Assert.True(theft.IsFailure);
        Assert.Equal(MessageErrors.NotSender, theft.Error);
    }

    [Fact]
    public async Task Delete_allows_admin_for_others_messages()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("del.owner");
        var member = await ctx.SeedUserAsync("del.member");
        var chat = await SeedGroupAsync(ctx, owner, member);
        var sent = await SendAsync(ctx, member, chat.Id, "bye", "k-1");

        var deleted = await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(owner))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);
        Assert.True(deleted.IsSuccess);
        Assert.True(deleted.Value!.IsDeleted);
    }

    [Fact]
    public async Task Member_cannot_delete_others_messages()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("del2.a");
        var b = await ctx.SeedUserAsync("del2.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var sent = await SendAsync(ctx, a, chat.Id, "mine", "k-1");

        var result = await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(b))
            .Handle(new DeleteMessageCommand(sent.Id), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal(MessageErrors.CannotDelete, result.Error);
    }

    [Fact]
    public async Task Reply_links_to_target_and_forward_carries_provenance()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("rf.a");
        var b = await ctx.SeedUserAsync("rf.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var first = await SendAsync(ctx, a, chat.Id, "question", "k-1");

        var reply = await new SendMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(b))
            .Handle(new SendMessageCommand(chat.Id, "answer", "k-2", first.Id), CancellationToken.None);
        Assert.True(reply.IsSuccess);
        Assert.Equal(MessageKind.Reply.ToString(), reply.Value!.Kind);
        Assert.NotNull(reply.Value.ReplyTo);
        Assert.Equal(first.Id, reply.Value.ReplyTo!.MessageId);

        var other = await SeedGroupAsync(ctx, a, b);
        var fwd = await new ForwardMessageHandler(
                ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a))
            .Handle(new ForwardMessageCommand(chat.Id, first.Id, other.Id, "k-fwd"), CancellationToken.None);
        Assert.True(fwd.IsSuccess);
        Assert.Equal(MessageKind.Forward.ToString(), fwd.Value!.Kind);
        Assert.NotNull(fwd.Value.ForwardFrom);
        Assert.Equal(first.Id, fwd.Value.ForwardFrom!.MessageId);
        Assert.Equal("question", fwd.Value.Body);
    }

    [Fact]
    public async Task MarkRead_clears_unread_for_reader_only()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("read.a");
        var b = await ctx.SeedUserAsync("read.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        await SendAsync(ctx, a, chat.Id, "ping", "k-1");

        var before = await ctx.Chats.GetByIdAsync(chat.Id, CancellationToken.None);
        Assert.Equal(1, before!.GetMember(b.Id)!.UnreadCount);

        var read = await new MarkReadHandler(ctx.Chats, ctx.Users, ctx.Clock, ctx.As(b))
            .Handle(new MarkReadCommand(chat.Id, 1), CancellationToken.None);
        Assert.True(read.IsSuccess);
        Assert.Equal(0, read.Value!.UnreadCount);
        Assert.Equal(1, read.Value.LastReadSeq);
    }

    [Fact]
    public async Task Full_member_lifecycle()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("mem.owner");
        var member = await ctx.SeedUserAsync("mem.member");
        var newcomer = await ctx.SeedUserAsync("mem.new");
        var chat = await SeedGroupAsync(ctx, owner, member);

        var added = await new AddMemberHandler(ctx.Chats, ctx.Users, ctx.Clock, ctx.As(owner))
            .Handle(new AddMemberCommand(chat.Id, newcomer.Id), CancellationToken.None);
        Assert.True(added.IsSuccess);
        Assert.Equal(3, added.Value!.MemberCount);

        var nonAdmin = await new AddMemberHandler(ctx.Chats, ctx.Users, ctx.Clock, ctx.As(member))
            .Handle(new AddMemberCommand(chat.Id, newcomer.Id), CancellationToken.None);
        Assert.True(nonAdmin.IsFailure);
        Assert.Equal(ChatErrors.NotAdmin, nonAdmin.Error);

        var promoted = await new SetMemberRoleHandler(ctx.Chats, ctx.Users, ctx.Clock, ctx.As(owner))
            .Handle(new SetMemberRoleCommand(chat.Id, member.Id, "Admin"), CancellationToken.None);
        Assert.True(promoted.IsSuccess);

        var left = await new LeaveChatHandler(ctx.Chats, ctx.Clock, ctx.As(newcomer))
            .Handle(new LeaveChatCommand(chat.Id), CancellationToken.None);
        Assert.True(left.IsSuccess);
    }
}
