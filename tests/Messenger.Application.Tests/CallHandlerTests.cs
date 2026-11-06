using Messenger.Application.Calls;
using Messenger.Application.Features.Chats;
using Messenger.Application.Services;
using Messenger.Application.Services.Implementations;
using Messenger.Domain.Calls;
using Messenger.Domain.Chats;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

/// <summary>Call signaling: lifecycle, per-operation authorization, LiveKit token rules.</summary>
public sealed class CallHandlerTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private sealed class Ctx
    {
        public FakeUserStore Users = new();
        public FakeChatStore Chats = new();
        public FakeCallStore Calls = new();
        public FakeLiveKit LiveKit = new();
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

        public async Task<Chat> SeedDmAsync(User a, User b)
        {
            var svc = new ConversationService(Chats, Users, Ids, Clock);
            var result = await svc.CreateDmAsync(new CreateDmCommand(b.Id), new CallerContext(a.Id, null, []), CancellationToken.None);
            Assert.True(result.IsSuccess);
            return (await Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
        }

        public async Task<Chat> SeedGroupAsync(User owner, params User[] members)
        {
            var ids = new List<string> { owner.Id };
            ids.AddRange(members.Select(m => m.Id));
            var svc = new ConversationService(Chats, Users, Ids, Clock);
            var result = await svc.CreateGroupAsync(
                new CreateGroupCommand("Team", ids, null), new CallerContext(owner.Id, null, []), CancellationToken.None);
            Assert.True(result.IsSuccess);
            return (await Chats.GetByIdAsync(result.Value!.Id, CancellationToken.None))!;
        }

        public CallerContext As(User user) => new(user.Id, null, []);
    }

    private static Task<CallDto> StartAsync(Ctx ctx, Chat chat, User user, string media = "Video")
    {
        var svc = new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock);
        return AssertOk(svc.StartCallAsync(new StartCallCommand(chat.Id, media), ctx.As(user), CancellationToken.None));
    }

    private static async Task<T> AssertOk<T>(Task<Domain.Common.Result<T>> task)
    {
        var result = await task;
        Assert.True(result.IsSuccess);
        return result.Value!;
    }

    [Fact]
    public async Task Start_creates_ringing_call_for_members()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("call.a");
        var b = await ctx.SeedUserAsync("call.b");
        var chat = await ctx.SeedDmAsync(a, b);

        var call = await StartAsync(ctx, chat, a, "Audio");

        Assert.Equal(chat.Id, call.ChatId);
        Assert.Equal("Audio", call.MediaType);
        Assert.Equal("Ringing", call.Status);
        Assert.Equal(2, call.Participants.Count);
    }

    [Fact]
    public async Task Start_rejects_second_live_call()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("busy.a");
        var b = await ctx.SeedUserAsync("busy.b");
        var chat = await ctx.SeedDmAsync(a, b);
        await StartAsync(ctx, chat, a);

        var svc = new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock);
        var result = await svc.StartCallAsync(new StartCallCommand(chat.Id, "Audio"), ctx.As(b), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CallErrors.AlreadyActive, result.Error);
    }

    [Fact]
    public async Task Start_lost_insert_race_reports_busy()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("race.a");
        var b = await ctx.SeedUserAsync("race.b");
        var chat = await ctx.SeedDmAsync(a, b);
        // Pre-check misses (no live call yet) but the insert collides: the
        // concurrent winner owns the ring.
        ctx.Calls.ThrowOnAdd = true;

        var svc = new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock);
        var result = await svc.StartCallAsync(new StartCallCommand(chat.Id, "Audio"), ctx.As(a), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CallErrors.AlreadyActive, result.Error);
    }

    [Fact]
    public async Task Start_forbids_outsiders()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("priv.a");
        var b = await ctx.SeedUserAsync("priv.b");
        var outsider = await ctx.SeedUserAsync("priv.out");
        var chat = await ctx.SeedDmAsync(a, b);

        var svc = new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock);
        var result = await svc.StartCallAsync(new StartCallCommand(chat.Id, "Video"), ctx.As(outsider), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CallErrors.CannotStart, result.Error);
    }

    [Fact]
    public async Task Channel_subscriber_cannot_start_but_owner_can()
    {
        var ctx = new Ctx();
        var owner = await ctx.SeedUserAsync("ch.owner");
        var sub = await ctx.SeedUserAsync("ch.subuser");
        var conversations = new ConversationService(
            ctx.Chats, ctx.Users, ctx.Ids, ctx.Clock);
        var created = await conversations.CreateChannelAsync(
            new CreateChannelCommand("News", null, [sub.Id], false, null), ctx.As(owner), CancellationToken.None);
        Assert.True(created.IsSuccess);
        var chat = (await ctx.Chats.GetByIdAsync(created.Value!.Id, CancellationToken.None))!;

        var calls = new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock);
        var denied = await calls.StartCallAsync(new StartCallCommand(chat.Id, "Audio"), ctx.As(sub), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(CallErrors.CannotStart, denied.Error);

        var allowed = await StartAsync(ctx, chat, owner, "Audio");
        Assert.Equal("Ringing", allowed.Status);
    }

    [Fact]
    public async Task Accept_join_leave_flow_activates_then_auto_ends()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("flow.a");
        var b = await ctx.SeedUserAsync("flow.b");
        var chat = await ctx.SeedDmAsync(a, b);
        var call = await StartAsync(ctx, chat, a);

        var accepted = await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .AcceptCallAsync(new AcceptCallCommand(call.Id), ctx.As(b), CancellationToken.None));
        Assert.Equal("Accepted", accepted.Participants.Single(p => p.UserId == b.Id).Status);

        var initiator = await new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .JoinCallAsync(new JoinCallCommand(call.Id), ctx.As(a), CancellationToken.None);
        Assert.True(initiator.IsSuccess);
        Assert.Equal("Active", initiator.Value!.Call.Status);

        var joined = await new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .JoinCallAsync(new JoinCallCommand(call.Id), ctx.As(b), CancellationToken.None);
        Assert.True(joined.IsSuccess);
        Assert.Equal("Active", joined.Value!.Call.Status);
        Assert.Equal("wss://livekit.test", joined.Value.Url);
        Assert.Equal(call.RoomName, joined.Value.RoomName);
        Assert.Single(ctx.LiveKit.Issued.Where(i => i.Identity == b.Id));

        // Callee leaves: call stays live while the initiator remains joined.
        var left = await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .LeaveCallAsync(new LeaveCallCommand(call.Id), ctx.As(b), CancellationToken.None));
        Assert.Equal("Active", left.Status);
        Assert.False(left.Participants.Single(p => p.UserId == b.Id).IsJoined);

        // Last one out ends it for everyone.
        var end = await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .LeaveCallAsync(new LeaveCallCommand(call.Id), ctx.As(a), CancellationToken.None));
        Assert.Equal("Ended", end.Status);
        Assert.Equal("Completed", end.EndReason);
    }

    [Fact]
    public async Task Dm_reject_ends_call()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("rej.a");
        var b = await ctx.SeedUserAsync("rej.b");
        var chat = await ctx.SeedDmAsync(a, b);
        var call = await StartAsync(ctx, chat, a);

        var rejected = await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .RejectCallAsync(new RejectCallCommand(call.Id), ctx.As(b), CancellationToken.None));

        Assert.Equal("Ended", rejected.Status);
        Assert.Equal("Declined", rejected.EndReason);
    }

    [Fact]
    public async Task Group_reject_keeps_call_ringing_for_others()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("gr.a");
        var b = await ctx.SeedUserAsync("gr.b");
        var c = await ctx.SeedUserAsync("gr.c");
        var chat = await ctx.SeedGroupAsync(a, b, c);
        var call = await StartAsync(ctx, chat, a);

        var rejected = await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .RejectCallAsync(new RejectCallCommand(call.Id), ctx.As(b), CancellationToken.None));

        Assert.Equal("Ringing", rejected.Status);
        Assert.Equal("Rejected", rejected.Participants.Single(p => p.UserId == b.Id).Status);
    }

    [Fact]
    public async Task Join_fails_closed_without_livekit()
    {
        var ctx = new Ctx();
        ctx.LiveKit.IsConfigured = false;
        var a = await ctx.SeedUserAsync("nolk.a");
        var b = await ctx.SeedUserAsync("nolk.b");
        var chat = await ctx.SeedDmAsync(a, b);
        var call = await StartAsync(ctx, chat, a);

        var result = await new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .JoinCallAsync(new JoinCallCommand(call.Id), ctx.As(b), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CallErrors.LiveKitNotConfigured, result.Error);

        // No state mutated: the call still rings, retry succeeds once configured.
        var stored = await ctx.Calls.GetByIdAsync(call.Id, CancellationToken.None);
        Assert.Equal(CallStatus.Ringing, stored!.Status);
    }

    [Fact]
    public async Task End_rules_dm_either_party_group_initiator_or_admin()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("end.a");
        var b = await ctx.SeedUserAsync("end.b");
        var c = await ctx.SeedUserAsync("end.c");
        var dm = await ctx.SeedDmAsync(a, b);
        var dmCall = await StartAsync(ctx, dm, a);

        // Either DM party may hang up.
        var dmEnd = await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .EndCallAsync(new EndCallCommand(dmCall.Id), ctx.As(b), CancellationToken.None));
        Assert.Equal("Ended", dmEnd.Status);
        Assert.Equal("Cancelled", dmEnd.EndReason); // never went active.

        var group = await ctx.SeedGroupAsync(a, b, c);
        var groupCall = await StartAsync(ctx, group, a);

        // Plain member cannot end a group call for everyone.
        var denied = await new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .EndCallAsync(new EndCallCommand(groupCall.Id), ctx.As(c), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(CallErrors.NotInitiator, denied.Error);

        // Initiator can.
        var ended = await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .EndCallAsync(new EndCallCommand(groupCall.Id), ctx.As(a), CancellationToken.None));
        Assert.Equal("Ended", ended.Status);
    }

    [Fact]
    public async Task History_lists_newest_first_for_members_only()
    {
        var ctx = new Ctx();
        var a = await ctx.SeedUserAsync("hist.a");
        var b = await ctx.SeedUserAsync("hist.b");
        var outsider = await ctx.SeedUserAsync("hist.out");
        var chat = await ctx.SeedDmAsync(a, b);
        var first = await StartAsync(ctx, chat, a);
        await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .EndCallAsync(new EndCallCommand(first.Id), ctx.As(a), CancellationToken.None));
        ctx.Clock.Now = Now.AddMinutes(5);
        await StartAsync(ctx, chat, b);

        var history = await AssertOk(new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .ListCallsAsync(new ListCallsQuery(chat.Id, 10), ctx.As(a), CancellationToken.None));
        Assert.Equal(2, history.Count);
        Assert.Equal(b.Id, history[0].InitiatorId);

        var denied = await new CallService(ctx.Chats, ctx.Calls, ctx.Users, ctx.LiveKit, ctx.Ids, ctx.Clock)
            .ListCallsAsync(new ListCallsQuery(chat.Id, 10), ctx.As(outsider), CancellationToken.None);
        Assert.True(denied.IsFailure);
        Assert.Equal(CallErrors.Forbidden, denied.Error);
    }
}
