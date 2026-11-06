using Messenger.Application.Features.Chats;
using Messenger.Application.Features.Messages;
using Messenger.Application.Features.Notifications;
using Messenger.Application.Features.Users;
using Messenger.Application.Messaging;
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

        public FakeCurrentUser As(User user, params string[] roles) =>
            new(user.Id, roles.Length == 0 ? user.Roles.Select(r => r.ToString()).ToList() : roles.ToList());
    }

    private static async Task<Chat> SeedGroupAsync(Ctx ctx, User owner, string title, params User[] members)
    {
        var ids = new List<string> { owner.Id };
        ids.AddRange(members.Select(m => m.Id));
        var handler = new CreateGroupHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(owner));
        var result = await handler.Handle(new CreateGroupCommand(title, ids, null), CancellationToken.None);
        Assert.True(result.IsSuccess);
        return (await ctx.Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
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

        await new DeleteMessageHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new DeleteMessageCommand(doomed.Id), CancellationToken.None);

        var handler = new SearchMessagesHandler(ctx.Chats, ctx.Messages, ctx.Files, ctx.Users, ctx.As(a));
        var result = await handler.Handle(new SearchMessagesQuery("release", 10), CancellationToken.None);

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

        var dmHandler = new CreateDmHandler(ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(a));
        var dm = await dmHandler.Handle(new CreateDmCommand(b.Id), CancellationToken.None);
        Assert.True(dm.IsSuccess);

        var handler = new SearchMyChatsHandler(ctx.Chats, ctx.Users, ctx.As(a));

        var byTitle = await handler.Handle(new SearchMyChatsQuery("release", 10), CancellationToken.None);
        Assert.True(byTitle.IsSuccess);
        Assert.Single(byTitle.Value!);
        Assert.Equal("Group", byTitle.Value![0].Type);

        var byPeer = await handler.Handle(new SearchMyChatsQuery("c.bob", 10), CancellationToken.None);
        Assert.True(byPeer.IsSuccess);
        Assert.Single(byPeer.Value!);
        Assert.Equal("Dm", byPeer.Value![0].Type);
    }

    [Fact]
    public async Task Settings_round_trip_with_validation()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("set.a");

        var get = await new GetMySettingsHandler(ctx.Users, ctx.As(a))
            .Handle(new GetMySettingsQuery(), CancellationToken.None);
        Assert.True(get.IsSuccess);
        Assert.Equal("system", get.Value!.Theme);
        Assert.True(get.Value.NotificationsEnabled);

        var update = await new UpdateMySettingsHandler(ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new UpdateMySettingsCommand("dark", false), CancellationToken.None);
        Assert.True(update.IsSuccess);
        Assert.Equal("dark", update.Value!.Theme);
        Assert.False(update.Value.NotificationsEnabled);

        var bad = await new UpdateMySettingsHandler(ctx.Users, ctx.Clock, ctx.As(a))
            .Handle(new UpdateMySettingsCommand("neon", true), CancellationToken.None);
        Assert.True(bad.IsFailure);
    }

    [Fact]
    public async Task Notifications_list_unread_with_titles()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("n.ali");
        var b = await ctx.SeedUserAsync("n.bob");
        var chat = await SeedGroupAsync(ctx, a, "Crew", b);
        await SendAsync(ctx, b, chat.Id, "ping", "k-1");

        var result = await new ListNotificationsHandler(ctx.Chats, ctx.Users, ctx.As(a))
            .Handle(new ListNotificationsQuery(20), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!);
        Assert.Equal(chat.Id, result.Value![0].ChatId);
        Assert.Equal(1, result.Value![0].UnreadCount);
        Assert.Equal("Crew", result.Value![0].Title);

        // Sender has nothing unread.
        var senderView = await new ListNotificationsHandler(ctx.Chats, ctx.Users, ctx.As(b))
            .Handle(new ListNotificationsQuery(20), CancellationToken.None);
        Assert.True(senderView.IsSuccess);
        Assert.Empty(senderView.Value!);
    }

    [Fact]
    public async Task Support_open_assign_resolve_flow_links_chat()
    {
        var ctx = new Ctx();
        var customer = await ctx.SeedUserAsync("sup.customer");
        var agent = await ctx.SeedUserAsync("sup.agent", Role.SupportAgent);

        var opened = await new OpenTicketHandler(ctx.Tickets, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(customer))
            .Handle(new OpenTicketCommand("Cannot log in", "App crashes"), CancellationToken.None);
        Assert.True(opened.IsSuccess);
        Assert.Equal("Open", opened.Value!.Status);

        var assigned = await new AssignTicketHandler(
                ctx.Tickets, ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(agent, "SupportAgent"))
            .Handle(new AssignTicketCommand(opened.Value.Id, null), CancellationToken.None);
        Assert.True(assigned.IsSuccess);
        Assert.Equal("Assigned", assigned.Value!.Status);
        Assert.Equal(agent.Id, assigned.Value.AssigneeId);
        Assert.NotNull(assigned.Value.ChatId);

        var chat = await ctx.Chats.GetByIdAsync(assigned.Value.ChatId!, CancellationToken.None);
        Assert.NotNull(chat);
        Assert.True(chat!.IsMember(customer.Id));
        Assert.True(chat.IsMember(agent.Id));

        var progressed = await new SetTicketStatusHandler(ctx.Tickets, ctx.Users, ctx.Clock, ctx.As(agent, "SupportAgent"))
            .Handle(new SetTicketStatusCommand(opened.Value.Id, "InProgress"), CancellationToken.None);
        Assert.True(progressed.IsSuccess);

        var resolved = await new SetTicketStatusHandler(ctx.Tickets, ctx.Users, ctx.Clock, ctx.As(agent, "SupportAgent"))
            .Handle(new SetTicketStatusCommand(opened.Value.Id, "Resolved"), CancellationToken.None);
        Assert.True(resolved.IsSuccess);

        // Customer closes (accepts resolution).
        var closed = await new SetTicketStatusHandler(ctx.Tickets, ctx.Users, ctx.Clock, ctx.As(customer))
            .Handle(new SetTicketStatusCommand(opened.Value.Id, "Closed"), CancellationToken.None);
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

        var t1 = await new OpenTicketHandler(ctx.Tickets, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(c1))
            .Handle(new OpenTicketCommand("Help A", null), CancellationToken.None);
        await new OpenTicketHandler(ctx.Tickets, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(c2))
            .Handle(new OpenTicketCommand("Help B", null), CancellationToken.None);

        // Customer sees only their own.
        var mine = await new ListTicketsHandler(ctx.Tickets, ctx.Users, ctx.As(c1))
            .Handle(new ListTicketsQuery(null, null, true, 20), CancellationToken.None);
        Assert.True(mine.IsSuccess);
        Assert.Single(mine.Value!);

        // Customer cannot read another's ticket.
        var peek = await new GetTicketHandler(ctx.Tickets, ctx.Users, ctx.As(c2))
            .Handle(new GetTicketQuery(t1.Value!.Id), CancellationToken.None);
        Assert.True(peek.IsFailure);
        Assert.Equal(SupportErrors.Forbidden, peek.Error);

        // Customer cannot assign or advance work.
        var assign = await new AssignTicketHandler(
                ctx.Tickets, ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock, ctx.As(c1))
            .Handle(new AssignTicketCommand(t1.Value.Id, null), CancellationToken.None);
        Assert.True(assign.IsFailure);
        Assert.Equal(SupportErrors.AgentOnly, assign.Error);

        // Agent sees the whole queue.
        var queue = await new ListTicketsHandler(ctx.Tickets, ctx.Users, ctx.As(agent, "SupportAgent"))
            .Handle(new ListTicketsQuery("Open", null, false, 20), CancellationToken.None);
        Assert.True(queue.IsSuccess);
        Assert.Equal(2, queue.Value!.Count);
    }
}
