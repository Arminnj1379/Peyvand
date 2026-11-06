using Messenger.Application.Features.Chats;
using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Messenger.Application.Services;
using Messenger.Application.Services.Implementations;
using Messenger.Domain.Chats;
using Messenger.Domain.Messages;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

/// <summary>Permission boundaries and business rules for the advanced slice:
/// profiles, visibility, invites, subscriptions, preferences, reactions,
/// threads, bulk delete, and blocks.</summary>
public sealed class AdvancedHandlerTests
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
        public FakeAvatarStorage Avatars = new();
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

        public CallerContext As(User user) => new(user.Id, null, []);
    }

    private static async Task<Chat> SeedGroupAsync(Ctx ctx, User owner, params User[] members)
    {
        var ids = new List<string> { owner.Id };
        ids.AddRange(members.Select(m => m.Id));
        var svc = new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock);
        var result = await svc.CreateGroupAsync(
            new CreateGroupCommand("Team", ids, null), ctx.As(owner), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return (await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
    }

    private static async Task<Chat> SeedChannelAsync(Ctx ctx, User owner, bool isPublic, string? username, params User[] subs)
    {
        var svc = new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock);
        var result = await svc.CreateChannelAsync(
            new CreateChannelCommand("News", null, subs.Select(s => s.Id).ToList(), isPublic, username),
            ctx.As(owner), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return (await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
    }

    private static async Task<MessageDto> SendAsync(Ctx ctx, User sender, string chatId, string body, string clientId, string? replyTo = null)
    {
        var svc = new MessageService(
            ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock);
        var result = await svc.SendMessageAsync(
            new SendMessageCommand(chatId, body, clientId, replyTo), ctx.As(sender), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task UpdateGroup_member_is_rejected()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("prof.owner");
        var member = await ctx.SeedUserAsync("prof.member");
        var chat = await SeedGroupAsync(ctx, owner, member);

        var denied = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .UpdateGroupAsync(new UpdateGroupCommand(chat.Id, "Hijacked", null, null), ctx.As(member), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(ChatErrors.NotAdmin, denied.Error);

        var ok = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .UpdateGroupAsync(new UpdateGroupCommand(chat.Id, "Renamed", "About", true), ctx.As(owner), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.Equal("Renamed", ok.Value!.Title);
        Assert.True(ok.Value.MembersCanInvite);
    }

    [Fact]
    public async Task Member_invite_allowed_only_when_group_enables_it()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("inv.owner");
        var member = await ctx.SeedUserAsync("inv.member");
        var newcomer = await ctx.SeedUserAsync("inv.new");
        var chat = await SeedGroupAsync(ctx, owner, member);

        var denied = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .AddMemberAsync(new AddMemberCommand(chat.Id, newcomer.Id), ctx.As(member), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(ChatErrors.NotAdmin, denied.Error);

        await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .UpdateGroupAsync(new UpdateGroupCommand(chat.Id, null, null, true), ctx.As(owner), CancellationToken.None);

        var allowed = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .AddMemberAsync(new AddMemberCommand(chat.Id, newcomer.Id), ctx.As(member), CancellationToken.None);
        Assert.True(allowed.IsSuccess);
        Assert.Equal(3, allowed.Value!.MemberCount);
    }

    [Fact]
    public async Task Channel_subscriber_cannot_invite()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("ci.owner");
        var sub = await ctx.SeedUserAsync("ci.sub");
        var stranger = await ctx.SeedUserAsync("ci.stranger");
        var chat = await SeedChannelAsync(ctx, owner, false, null, sub);

        var denied = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .AddMemberAsync(new AddMemberCommand(chat.Id, stranger.Id), ctx.As(sub), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(ChatErrors.NotAdmin, denied.Error);
    }

    [Fact]
    public async Task Visibility_username_conflict_is_rejected()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("vis.a");
        var b = await ctx.SeedUserAsync("vis.b");
        var chA = await SeedChannelAsync(ctx, a, true, "dailynews");
        var chB = await SeedChannelAsync(ctx, b, false, null);

        var clash = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .SetChannelVisibilityAsync(new SetChannelVisibilityCommand(chB.Id, true, "DailyNews"), ctx.As(b), CancellationToken.None);
        Assert.True(clash.IsFailure);
        Assert.Equal(ChatErrors.UsernameTaken, clash.Error);

        // Self re-claim is fine.
        var self = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .SetChannelVisibilityAsync(new SetChannelVisibilityCommand(chA.Id, true, "dailynews"), ctx.As(a), CancellationToken.None);
        Assert.True(self.IsSuccess);
    }

    [Fact]
    public async Task Invite_rotate_is_admin_only_and_join_assigns_role()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("lnk.owner");
        var member = await ctx.SeedUserAsync("lnk.member");
        var joiner = await ctx.SeedUserAsync("lnk.joiner");
        var chat = await SeedGroupAsync(ctx, owner, member);

        var denied = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .RotateInviteAsync(new RotateInviteCommand(chat.Id), ctx.As(member), CancellationToken.None);
        Assert.True(denied.IsFailure);

        var rotated = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .RotateInviteAsync(new RotateInviteCommand(chat.Id), ctx.As(owner), CancellationToken.None);
        Assert.True(rotated.IsSuccess);
        Assert.NotEmpty(rotated.Value!);

        var joined = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .JoinViaInviteAsync(new JoinViaInviteCommand(rotated.Value), ctx.As(joiner), CancellationToken.None);
        Assert.True(joined.IsSuccess);
        Assert.Contains(joined.Value!.Members, m => m.UserId == joiner.Id && m.Role == "Member");

        var badCode = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .JoinViaInviteAsync(new JoinViaInviteCommand("deadbeef"), ctx.As(joiner), CancellationToken.None);
        Assert.True(badCode.IsFailure);
        Assert.Equal(ChatErrors.InviteInvalid, badCode.Error);

        // Revoke kills the link.
        var revoked = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .RevokeInviteAsync(new RevokeInviteCommand(chat.Id), ctx.As(owner), CancellationToken.None);
        Assert.True(revoked.IsSuccess);
        var late = await ctx.SeedUserAsync("lnk.late");
        var afterRevoke = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .JoinViaInviteAsync(new JoinViaInviteCommand(rotated.Value), ctx.As(late), CancellationToken.None);
        Assert.True(afterRevoke.IsFailure);
        Assert.Equal(ChatErrors.InviteInvalid, afterRevoke.Error);
    }

    [Fact]
    public async Task Subscribe_public_ok_private_requires_invite()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("sub.owner");
        var fan = await ctx.SeedUserAsync("sub.fan");
        var pub = await SeedChannelAsync(ctx, owner, true, "openchan");
        var priv = await SeedChannelAsync(ctx, owner, false, null);

        var ok = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .SubscribeChannelAsync(new SubscribeChannelCommand(pub.Id), ctx.As(fan), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.Contains(ok.Value!.Members, m => m.UserId == fan.Id && m.Role == "Subscriber");

        var again = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .SubscribeChannelAsync(new SubscribeChannelCommand(pub.Id), ctx.As(fan), CancellationToken.None);
        Assert.True(again.IsFailure);
        Assert.Equal(ChatErrors.AlreadySubscribed, again.Error);

        var denied = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .SubscribeChannelAsync(new SubscribeChannelCommand(priv.Id), ctx.As(fan), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(ChatErrors.InviteRequired, denied.Error);

        var left = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .UnsubscribeChannelAsync(new UnsubscribeChannelCommand(pub.Id), ctx.As(fan), CancellationToken.None);
        Assert.True(left.IsSuccess);

        var notSubbed = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .UnsubscribeChannelAsync(new UnsubscribeChannelCommand(pub.Id), ctx.As(fan), CancellationToken.None);
        Assert.True(notSubbed.IsFailure);
        Assert.Equal(ChatErrors.NotSubscribed, notSubbed.Error);
    }

    [Fact]
    public async Task Preferences_are_caller_relative()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("pref.a");
        var b = await ctx.SeedUserAsync("pref.b");
        var chat = await SeedGroupAsync(ctx, a, b);

        var muted = await new ChatInteractionService(ctx.Chats, ctx.Messages, ctx.Users, ctx.Clock)
            .SetMuteAsync(new SetMuteCommand(chat.Id, true), ctx.As(b), CancellationToken.None);
        Assert.True(muted.IsSuccess);
        Assert.True(muted.Value!.IsMuted);

        var archived = await new ChatInteractionService(ctx.Chats, ctx.Messages, ctx.Users, ctx.Clock)
            .SetArchiveAsync(new SetArchiveCommand(chat.Id, true), ctx.As(b), CancellationToken.None);
        Assert.True(archived.IsSuccess);
        Assert.True(archived.Value!.IsArchived);

        // The other member sees no change.
        var peer = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .GetChatAsync(new GetChatQuery(chat.Id), ctx.As(a), CancellationToken.None);
        Assert.True(peer.IsSuccess);
        Assert.False(peer.Value!.IsMuted);
        Assert.False(peer.Value.IsArchived);
    }

    [Fact]
    public async Task MarkUnread_moves_watermark_back()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("unr.a");
        var b = await ctx.SeedUserAsync("unr.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        await SendAsync(ctx, a, chat.Id, "one", "k-1");
        await SendAsync(ctx, a, chat.Id, "two", "k-2");

        await new ChatInteractionService(ctx.Chats, ctx.Messages, ctx.Users, ctx.Clock)
            .MarkReadAsync(new MarkReadCommand(chat.Id, 2), ctx.As(b), CancellationToken.None);

        var unread = await new ChatInteractionService(ctx.Chats, ctx.Messages, ctx.Users, ctx.Clock)
            .MarkUnreadAsync(new MarkUnreadCommand(chat.Id, 2), ctx.As(b), CancellationToken.None);
        Assert.True(unread.IsSuccess);
        Assert.Equal(1, unread.Value!.LastReadSeq);
        Assert.Equal(1, unread.Value.UnreadCount);
    }

    [Fact]
    public async Task Subscriber_can_react_outsider_cannot()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("rx.owner");
        var sub = await ctx.SeedUserAsync("rx.sub");
        var outsider = await ctx.SeedUserAsync("rx.out");
        var chat = await SeedChannelAsync(ctx, owner, false, null, sub);
        var msg = await SendAsync(ctx, owner, chat.Id, "broadcast", "k-1");

        var ok = await new MessageService(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock)
            .ToggleReactionAsync(new ToggleReactionCommand(msg.Id, "👍"), ctx.As(sub), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.Single(ok.Value!.Reactions);
        Assert.Equal(1, ok.Value.Reactions[0].Count);
        Assert.True(ok.Value.Reactions[0].Mine);

        var denied = await new MessageService(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock)
            .ToggleReactionAsync(new ToggleReactionCommand(msg.Id, "👍"), ctx.As(outsider), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(ChatErrors.Forbidden, denied.Error);
    }

    [Fact]
    public async Task Reply_increments_thread_counter_and_lists_replies()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("thr.a");
        var b = await ctx.SeedUserAsync("thr.b");
        var chat = await SeedGroupAsync(ctx, a, b);
        var parent = await SendAsync(ctx, a, chat.Id, "question", "k-1");
        var child = await SendAsync(ctx, b, chat.Id, "answer", "k-2", parent.Id);

        Assert.Equal(1, child.ReplyTo is null ? -1 : 1); // reply link present
        var stored = await ctx.Messages.GetByIdAsync(parent.Id, CancellationToken.None);
        Assert.Equal(1, stored!.ReplyCount);

        var thread = await new MessageService(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock)
            .GetRepliesAsync(new GetRepliesQuery(parent.Id, 10), ctx.As(a), CancellationToken.None);
        Assert.True(thread.IsSuccess);
        Assert.Single(thread.Value!);
        Assert.Equal(child.Id, thread.Value![0].Id);
    }

    [Fact]
    public async Task BulkDelete_enforces_per_message_authorization()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("blk.owner");
        var member = await ctx.SeedUserAsync("blk.member");
        var chat = await SeedGroupAsync(ctx, owner, member);
        var m1 = await SendAsync(ctx, member, chat.Id, "m1", "k-1");
        var m2 = await SendAsync(ctx, owner, chat.Id, "m2", "k-2");

        // Member cannot bulk-delete someone else's message.
        var denied = await new MessageService(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock)
            .BulkDeleteMessagesAsync(new BulkDeleteMessagesCommand([m1.Id, m2.Id]), ctx.As(member), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(MessageErrors.CannotDelete, denied.Error);

        // Owner (admin) can delete both.
        var ok = await new MessageService(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock)
            .BulkDeleteMessagesAsync(new BulkDeleteMessagesCommand([m1.Id, m2.Id]), ctx.As(owner), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.Equal(2, ok.Value!.Count);
    }

    [Fact]
    public async Task DeleteGroup_owner_only_and_history_survives()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("del.owner");
        var admin = await ctx.SeedUserAsync("del.admin");
        var member = await ctx.SeedUserAsync("del.member");
        var chat = await SeedGroupAsync(ctx, owner, admin, member);
        var sent = await SendAsync(ctx, member, chat.Id, "keep me", "k-1");

        // Promote one member to admin: admins still cannot delete.
        var promoted = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .SetMemberRoleAsync(new SetMemberRoleCommand(chat.Id, admin.Id, "Admin"), ctx.As(owner), CancellationToken.None);
        Assert.True(promoted.IsSuccess);

        var adminDenied = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .DeleteGroupAsync(new DeleteGroupCommand(chat.Id), ctx.As(admin), CancellationToken.None);
        Assert.True(adminDenied.IsFailure);
        Assert.Equal(ChatErrors.OnlyOwner, adminDenied.Error);

        var memberDenied = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .DeleteGroupAsync(new DeleteGroupCommand(chat.Id), ctx.As(member), CancellationToken.None);
        Assert.True(memberDenied.IsFailure);
        Assert.Equal(ChatErrors.OnlyOwner, memberDenied.Error);

        var ok = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .DeleteGroupAsync(new DeleteGroupCommand(chat.Id), ctx.As(owner), CancellationToken.None);
        Assert.True(ok.IsSuccess);

        // Soft-delete: the tracked doc is flagged, history is preserved,
        // and deleted chats are invisible to reads (same as production).
        Assert.True(chat.IsDeleted);
        var stored = await ctx.Chats.GetByIdAsync(chat.Id, CancellationToken.None);
        Assert.Null(stored);
        var history = await ctx.Messages.GetByIdAsync(sent.Id, CancellationToken.None);
        Assert.NotNull(history);
        Assert.False(history!.IsDeleted);

        // A deleted group reads back as not-found (no member access leaks).
        var again = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .DeleteGroupAsync(new DeleteGroupCommand(chat.Id), ctx.As(owner), CancellationToken.None);
        Assert.True(again.IsFailure);
        Assert.Equal(ChatErrors.NotFound, again.Error);
    }

    [Fact]
    public async Task DeleteGroup_outsider_and_dm_are_rejected()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("del2.owner");
        var member = await ctx.SeedUserAsync("del2.member");
        var outsider = await ctx.SeedUserAsync("del2.out");
        var chat = await SeedGroupAsync(ctx, owner, member);

        var forbidden = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .DeleteGroupAsync(new DeleteGroupCommand(chat.Id), ctx.As(outsider), CancellationToken.None);
        Assert.True(forbidden.IsFailure);
        Assert.Equal(ChatErrors.Forbidden, forbidden.Error);

        var dm = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .CreateDmAsync(new CreateDmCommand(member.Id), ctx.As(owner), CancellationToken.None);
        Assert.True(dm.IsSuccess);

        var dmDenied = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .DeleteGroupAsync(new DeleteGroupCommand(dm.Value!.Id), ctx.As(owner), CancellationToken.None);
        Assert.True(dmDenied.IsFailure);
        Assert.Equal(ChatErrors.DmMembershipFixed, dmDenied.Error);
    }

    [Fact]
    public async Task Block_prevents_dm_create_and_send()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("bl.a");
        var b = await ctx.SeedUserAsync("bl.b");

        Assert.True(b.Block(a.Id, Now).IsSuccess);
        await ctx.Users.UpdateAsync(b, CancellationToken.None);

        var dm = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .CreateDmAsync(new CreateDmCommand(b.Id), ctx.As(a), CancellationToken.None);
        Assert.True(dm.IsFailure);
        Assert.Equal(ChatErrors.Blocked, dm.Error);

        // Unblocked pair works, then a later block stops sends.
        b.Unblock(a.Id, Now);
        await ctx.Users.UpdateAsync(b, CancellationToken.None);
        var ok = await new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock)
            .CreateDmAsync(new CreateDmCommand(b.Id), ctx.As(a), CancellationToken.None);
        Assert.True(ok.IsSuccess);

        Assert.True(a.Block(b.Id, Now).IsSuccess);
        await ctx.Users.UpdateAsync(a, CancellationToken.None);
        var send = await new MessageService(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock)
            .SendMessageAsync(new SendMessageCommand(ok.Value!.Id, "hi", "k-1", null), ctx.As(b), CancellationToken.None);
        Assert.True(send.IsFailure);
        Assert.Equal(ChatErrors.Blocked, send.Error);
    }

    [Fact]
    public async Task Chat_avatar_upload_is_admin_only()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("av.owner");
        var member = await ctx.SeedUserAsync("av.member");
        var chat = await SeedGroupAsync(ctx, owner, member);
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        var denied = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .UploadChatAvatarAsync(new UploadChatAvatarCommand(chat.Id, png, "image/png"), ctx.As(member), CancellationToken.None);
        Assert.True(denied.IsFailure);

        var ok = await new GroupService(ctx.Chats, ctx.Users, ctx.Avatars, ctx.Clock)
            .UploadChatAvatarAsync(new UploadChatAvatarCommand(chat.Id, png, "image/png"), ctx.As(owner), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.NotNull(ok.Value!.AvatarUrl);
    }
}
