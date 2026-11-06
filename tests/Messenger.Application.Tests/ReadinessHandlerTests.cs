using FluentValidation;
using Messenger.Application.Features.Chats;
using Messenger.Application.Features.Messages;
using Messenger.Application.Features.Notifications;
using Messenger.Application.Features.Users;
using Messenger.Application.Messaging;
using Messenger.Application.Services;
using Messenger.Application.Services.Implementations;
using Messenger.Application.Support;
using Messenger.Domain.Chats;
using Messenger.Domain.Messages;
using Messenger.Domain.Support;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

/// <summary>Readiness slice: search scoping, settings, notifications, support workflow.</summary>
public sealed class ReadinessHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Ctx
    {
        public FakeUserStore Users = new();
        public FakeChatStore Chats = new();
        public FakeMessageStore Messages = new();
        public FakeFileStore Files = new();
        public FakeTicketStore Tickets = new();
        public FakeAvatarStorage Avatars = new();
        public FakeClock Clock = new(Now);
        public FakeIds Ids = new();

        public async Task<User> SeedUserAsync(string username, params Role[] roles)
        {
            var registered = User.Register(
                Ids.NewId(), username, username, "H:pw", UserType.Employee, roles, null, Now);
            Assert.True(registered.IsSuccess);
            await Users.AddAsync(registered.Value!, CancellationToken.None);
            return registered.Value!;
        }

        public CallerContext As(User user, params string[] roles) =>
            new CallerContext(user.Id, null, roles.Length == 0 ? user.Roles.Select(r => r.ToString()).ToList() : roles.ToList());
    }

    private static async Task<Chat> SeedGroupAsync(Ctx ctx, User owner, string title, params User[] members)
    {
        var ids = new List<string> { owner.Id };
        ids.AddRange(members.Select(m => m.Id));
        var conversations = new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock);
        var result = await conversations.CreateGroupAsync(new CreateGroupCommand(title, ids, null), ctx.As(owner), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return (await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
    }

    private static async Task<MessageDto> SendAsync(Ctx ctx, User sender, string chatId, string body, string clientId)
    {
        var messages = new MessageService(
            ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock);
        var result = await messages.SendMessageAsync(
            new SendMessageCommand(chatId, body, clientId, null), ctx.As(sender), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task Message_search_scopes_to_caller_chats_and_skips_deleted()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("s.a");
        var b = await ctx.SeedUserAsync("s.b");
        var outsider = await ctx.SeedUserAsync("s.out");
        var mine = await SeedGroupAsync(ctx, a, "Team", b);
        var theirs = await SeedGroupAsync(ctx, outsider, "Others", b);

        await SendAsync(ctx, a, mine.Id, "deploy the release candidate", "k-1");
        var doomed = await SendAsync(ctx, a, mine.Id, "release notes draft", "k-2");
        await SendAsync(ctx, outsider, theirs.Id, "release the hounds", "k-3");

        await new MessageService(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock)
            .DeleteMessageAsync(new DeleteMessageCommand(doomed.Id), ctx.As(a), CancellationToken.None);

        var messages = new MessageService(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Ids, ctx.Clock);
        var result = await messages.SearchMessagesAsync(new SearchMessagesQuery("release", 10), ctx.As(a), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Own chat only (outsider chat excluded), deleted excluded.
        Assert.Single(result.Value!);
        Assert.Equal("deploy the release candidate", result.Value![0].Body);
    }

    [Fact]
    public async Task Chat_search_matches_titles_and_dm_peers()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("c.ali");
        var b = await ctx.SeedUserAsync("c.bob");
        await SeedGroupAsync(ctx, a, "Release Crew", b);

        var conversations = new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock);
        var dm = await conversations.CreateDmAsync(new CreateDmCommand(b.Id), ctx.As(a), CancellationToken.None);
        Assert.True(dm.IsSuccess);

        var searcher = new ConversationService(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock);

        var byTitle = await searcher.SearchMyChatsAsync(new SearchMyChatsQuery("release", 10), ctx.As(a), CancellationToken.None);
        Assert.True(byTitle.IsSuccess);
        Assert.Single(byTitle.Value!);
        Assert.Equal("Group", byTitle.Value![0].Type);

        var byPeer = await searcher.SearchMyChatsAsync(new SearchMyChatsQuery("c.bob", 10), ctx.As(a), CancellationToken.None);
        Assert.True(byPeer.IsSuccess);
        Assert.Single(byPeer.Value!);
        Assert.Equal("Dm", byPeer.Value![0].Type);
    }

    [Fact]
    public async Task Settings_round_trip_with_validation()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("set.a");

        var users = new UserService(ctx.Users, ctx.Avatars, ctx.Chats, ctx.Clock);
        var get = await users.GetMySettingsAsync(ctx.As(a), CancellationToken.None);
        Assert.True(get.IsSuccess);
        Assert.Equal("system", get.Value!.Theme);
        Assert.True(get.Value.NotificationsEnabled);

        var update = await users.UpdateMySettingsAsync(new UpdateMySettingsCommand("dark", false), ctx.As(a), CancellationToken.None);
        Assert.True(update.IsSuccess);
        Assert.Equal("dark", update.Value!.Theme);
        Assert.False(update.Value.NotificationsEnabled);

        var bad = await Assert.ThrowsAsync<ValidationException>(() =>
            users.UpdateMySettingsAsync(new UpdateMySettingsCommand("neon", true), ctx.As(a), CancellationToken.None));
    }

    [Fact]
    public async Task Notifications_list_unread_with_titles()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("n.ali");
        var b = await ctx.SeedUserAsync("n.bob");
        var chat = await SeedGroupAsync(ctx, a, "Crew", b);
        await SendAsync(ctx, b, chat.Id, "ping", "k-1");

        var notifications = new NotificationService(ctx.Chats, ctx.Users);
        var result = await notifications.ListNotificationsAsync(new ListNotificationsQuery(20), ctx.As(a), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(chat.Id, result.Value![0].ChatId);
        Assert.Equal(1, result.Value![0].UnreadCount);
        Assert.Equal("Crew", result.Value![0].Title);

        // Sender has nothing unread.
        var senderView = await notifications.ListNotificationsAsync(new ListNotificationsQuery(20), ctx.As(b), CancellationToken.None);
        Assert.True(senderView.IsSuccess);
        Assert.Empty(senderView.Value!);
    }

    [Fact]
    public async Task Support_open_assign_resolve_flow_links_chat()
    {
        var ctx = new Ctx();
        var customer = await ctx.SeedUserAsync("sup.customer");
        var agent = await ctx.SeedUserAsync("sup.agent", Role.SupportAgent);

        var support = new SupportService(ctx.Tickets, ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock);
        var opened = await support.OpenTicketAsync(
            new OpenTicketCommand("Cannot log in", "App crashes"), ctx.As(customer), CancellationToken.None);
        Assert.True(opened.IsSuccess);
        Assert.Equal("Open", opened.Value!.Status);

        var assigned = await support.AssignTicketAsync(
            new AssignTicketCommand(opened.Value.Id, null), ctx.As(agent, "SupportAgent"), CancellationToken.None);
        Assert.True(assigned.IsSuccess);
        Assert.Equal("Assigned", assigned.Value!.Status);
        Assert.Equal(agent.Id, assigned.Value.AssigneeId);
        Assert.NotNull(assigned.Value.ChatId);

        var chat = await ctx.Chats.GetByIdAsync(assigned.Value.ChatId!, CancellationToken.None);
        Assert.NotNull(chat);
        Assert.True(chat!.IsMember(customer.Id));
        Assert.True(chat.IsMember(agent.Id));

        var progressed = await support.SetTicketStatusAsync(
            new SetTicketStatusCommand(opened.Value.Id, "InProgress"), ctx.As(agent, "SupportAgent"), CancellationToken.None);
        Assert.True(progressed.IsSuccess);

        var resolved = await support.SetTicketStatusAsync(
            new SetTicketStatusCommand(opened.Value.Id, "Resolved"), ctx.As(agent, "SupportAgent"), CancellationToken.None);
        Assert.True(resolved.IsSuccess);

        // Customer closes (accepts resolution).
        var closed = await support.SetTicketStatusAsync(
            new SetTicketStatusCommand(opened.Value.Id, "Closed"), ctx.As(customer), CancellationToken.None);
        Assert.True(closed.IsSuccess);
        Assert.NotNull(closed.Value!.ClosedAt);
    }

    [Fact]
    public async Task Support_authorization_customer_isolation_and_agent_queue()
    {
        var ctx = new Ctx();
        var c1 = await ctx.SeedUserAsync("iso.c1");
        var c2 = await ctx.SeedUserAsync("iso.c2");
        var agent = await ctx.SeedUserAsync("iso.agent", Role.SupportAgent);

        var support = new SupportService(ctx.Tickets, ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock);
        var t1 = await support.OpenTicketAsync(
            new OpenTicketCommand("Help A", null), ctx.As(c1), CancellationToken.None);
        await support.OpenTicketAsync(
            new OpenTicketCommand("Help B", null), ctx.As(c2), CancellationToken.None);

        // Customer sees only their own.
        var mine = await support.ListTicketsAsync(
            new ListTicketsQuery(null, null, true, 20), ctx.As(c1), CancellationToken.None);
        Assert.True(mine.IsSuccess);
        Assert.Single(mine.Value!);

        // Customer cannot read another's ticket.
        var peek = await support.GetTicketAsync(
            new GetTicketQuery(t1.Value!.Id), ctx.As(c2), CancellationToken.None);
        Assert.True(peek.IsFailure);
        Assert.Equal(SupportErrors.Forbidden, peek.Error);

        // Customer cannot assign or advance work.
        var assign = await support.AssignTicketAsync(
            new AssignTicketCommand(t1.Value.Id, null), ctx.As(c1), CancellationToken.None);
        Assert.True(assign.IsFailure);
        Assert.Equal(SupportErrors.AgentOnly, assign.Error);

        // Agent sees the whole queue.
        var queue = await support.ListTicketsAsync(
            new ListTicketsQuery("Open", null, false, 20), ctx.As(agent, "SupportAgent"), CancellationToken.None);
        Assert.True(queue.IsSuccess);
        Assert.Equal(2, queue.Value!.Count);
    }
}
