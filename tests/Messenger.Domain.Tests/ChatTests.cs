using Messenger.Domain.Chats;

namespace Messenger.Domain.Tests;

public sealed class ChatTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CreateDm_builds_two_member_chat()
    {
        var result = Chat.CreateDm("c1", "u1", "u2", Now);

        Assert.True(result.IsSuccess);
        var chat = result.Value!;
        Assert.Equal(ChatType.Dm, chat.Type);
        Assert.Equal(2, chat.MemberCount);
        Assert.True(chat.IsMember("u1"));
        Assert.True(chat.IsMember("u2"));
        Assert.Equal(0, chat.SeqCounter);
    }

    [Fact]
    public void CreateDm_with_self_fails()
    {
        var result = Chat.CreateDm("c1", "u1", "u1", Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ChatErrors.CannotDmSelf, result.Error);
    }

    [Fact]
    public void DmKey_is_deterministic_pair_identity()
    {
        var forward = Chat.CreateDm("c1", "u1", "u2", Now).Value!;
        var reversed = Chat.CreateDm("c2", "u2", "u1", Now).Value!;

        Assert.Equal(Chat.BuildDmKey("u1", "u2"), forward.DmKey);
        Assert.Equal(forward.DmKey, reversed.DmKey);
    }

    [Fact]
    public void DmKey_is_null_for_groups()
    {
        var group = Chat.CreateGroup("g1", "u1", "Team", ["u1", "u2"], null, Now).Value!;

        Assert.Null(group.DmKey);
    }

    [Fact]
    public void Dm_membership_is_fixed()
    {
        var chat = Chat.CreateDm("c1", "u1", "u2", Now).Value!;

        Assert.True(chat.AddMember("u3", ChatRole.Member, Now).IsFailure);
        Assert.True(chat.RemoveMember("u2", Now).IsFailure);
        Assert.Equal(ChatErrors.DmMembershipFixed, chat.AddMember("u3", ChatRole.Member, Now).Error);
    }

    [Fact]
    public void CreateGroup_requires_title_and_distinct_members()
    {
        var bad = Chat.CreateGroup("c1", "u1", "  ", ["u1", "u2"], null, Now);
        Assert.True(bad.IsFailure);
        Assert.Equal(ChatErrors.InvalidTitle, bad.Error);

        var ok = Chat.CreateGroup("c1", "u1", "Team", ["u2", "u3", "u2"], "desc", Now);
        Assert.True(ok.IsSuccess);
        Assert.Equal(ChatRole.Owner, ok.Value!.GetMember("u1")!.Role);
        Assert.Equal(ChatRole.Member, ok.Value.GetMember("u2")!.Role);
        Assert.Equal(3, ok.Value.MemberCount);
    }

    [Fact]
    public void Channel_only_admins_can_post()
    {
        var chat = Chat.CreateChannel("c1", "u1", "News", null, ["u2"], Now).Value!;

        Assert.True(chat.CanPost("u1"));
        Assert.False(chat.CanPost("u2"));
        Assert.False(chat.CanPost("ghost"));
    }

    [Fact]
    public void AddMember_rejects_duplicates_and_caps_pins()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;

        Assert.Equal(ChatErrors.AlreadyMember, chat.AddMember("u2", ChatRole.Member, Now).Error);

        for (var i = 0; i < Chat.MaxPins; i++)
        {
            Assert.True(chat.Pin($"m{i}", Now).IsSuccess);
        }

        Assert.Equal(ChatErrors.PinLimit, chat.Pin("m-extra", Now).Error);
        Assert.Equal(ChatErrors.AlreadyPinned, chat.Pin("m0", Now).Error);
        Assert.True(chat.Unpin("m0", Now).IsSuccess);
        Assert.Equal(ChatErrors.NotPinned, chat.Unpin("m0", Now).Error);
    }

    [Fact]
    public void Sole_owner_cannot_be_removed_or_demoted()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;

        Assert.Equal(ChatErrors.SoleOwner, chat.RemoveMember("u1", Now).Error);
        Assert.Equal(ChatErrors.SoleOwner, chat.SetMemberRole("u1", ChatRole.Member, Now).Error);

        Assert.True(chat.SetMemberRole("u2", ChatRole.Owner, Now).IsSuccess);
        Assert.True(chat.RemoveMember("u1", Now).IsSuccess);
    }

    [Fact]
    public void ApplyNewMessage_advances_seq_and_unread()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;

        chat.ApplyNewMessage(1, "m1", "hello", "u1", Now);

        Assert.Equal(1, chat.SeqCounter);
        Assert.Equal("m1", chat.LastMessageId);
        Assert.Equal(0, chat.GetMember("u1")!.UnreadCount);
        Assert.Equal(1, chat.GetMember("u2")!.UnreadCount);
        Assert.Equal(1, chat.GetMember("u1")!.LastReadSeq);
    }

    [Fact]
    public void MarkRead_advances_watermark_only_forward()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;
        chat.ApplyNewMessage(1, "m1", "hello", "u1", Now);
        chat.ApplyNewMessage(2, "m2", "again", "u1", Now);

        Assert.True(chat.MarkRead("u2", 2, Now));
        Assert.Equal(0, chat.GetMember("u2")!.UnreadCount);
        Assert.False(chat.MarkRead("u2", 1, Now));
        Assert.False(chat.MarkRead("ghost", 2, Now));
    }

    [Fact]
    public void Rehydrate_round_trips_without_events()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;
        chat.ApplyNewMessage(3, "m3", "hi", "u2", Now);
        chat.Pin("m3", Now);

        var snapshot = new ChatSnapshot(
            chat.Id, chat.Type, chat.Title, chat.Description, chat.OwnerId,
            chat.Members.ToList(), chat.MemberCount, chat.PinnedMessageIds.ToList(),
            chat.LastMessageId, chat.LastMessageAt, chat.LastMessagePreview,
            chat.SeqCounter, chat.IsDeleted, chat.CreatedBy, chat.CreatedAt, chat.UpdatedAt);

        var revived = Chat.Rehydrate(snapshot);
        Assert.Equal(chat.Id, revived.Id);
        Assert.Equal(3, revived.SeqCounter);
        Assert.Equal(["m3"], revived.PinnedMessageIds);
        Assert.Empty(revived.DomainEvents);
    }

    [Fact]
    public void CreateChannel_public_requires_valid_username()
    {
        var bad = Chat.CreateChannel("c1", "u1", "News", null, null, Now, true, "ab");
        Assert.True(bad.IsFailure);
        Assert.Equal(ChatErrors.UsernameInvalid, bad.Error);

        var userOnPrivate = Chat.CreateChannel("c1", "u1", "News", null, null, Now, false, "news");
        Assert.True(userOnPrivate.IsFailure);
        Assert.Equal(ChatErrors.UsernameInvalid, userOnPrivate.Error);

        var ok = Chat.CreateChannel("c1", "u1", "News", null, null, Now, true, "News_Daily");
        Assert.True(ok.IsSuccess);
        Assert.True(ok.Value!.IsPublic);
        Assert.Equal("news_daily", ok.Value.Username);
    }

    [Fact]
    public void SetVisibility_switches_between_public_and_private()
    {
        var chat = Chat.CreateChannel("c1", "u1", "News", null, null, Now).Value!;
        Assert.False(chat.IsPublic);

        Assert.True(chat.SetVisibility(true, "newsroom", Now).IsSuccess);
        Assert.True(chat.IsPublic);
        Assert.Equal("newsroom", chat.Username);

        Assert.Equal(ChatErrors.UsernameInvalid, chat.SetVisibility(true, "x", Now).Error);

        Assert.True(chat.SetVisibility(false, null, Now).IsSuccess);
        Assert.False(chat.IsPublic);
        Assert.Null(chat.Username);

        var group = Chat.CreateGroup("g1", "u1", "Team", ["u2"], null, Now).Value!;
        Assert.Equal(ChatErrors.InvalidRole, group.SetVisibility(true, "team", Now).Error);
    }

    [Fact]
    public void Invite_rotate_and_revoke()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;
        Assert.Null(chat.InviteCode);

        Assert.True(chat.RotateInviteCode("code-1", Now).IsSuccess);
        Assert.Equal("code-1", chat.InviteCode);

        chat.RevokeInviteCode(Now);
        Assert.Null(chat.InviteCode);

        var dm = Chat.CreateDm("d1", "u1", "u2", Now).Value!;
        Assert.Equal(ChatErrors.DmMembershipFixed, dm.RotateInviteCode("x", Now).Error);
    }

    [Fact]
    public void Group_profile_update_validates_and_applies()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;

        Assert.True(chat.UpdateGroupProfile("New name", "New desc", true, Now).IsSuccess);
        Assert.Equal("New name", chat.Title);
        Assert.Equal("New desc", chat.Description);
        Assert.True(chat.MembersCanInvite);

        Assert.Equal(ChatErrors.InvalidTitle, chat.UpdateGroupProfile("  ", null, null, Now).Error);
        Assert.Equal(ChatErrors.InvalidDescription,
            chat.UpdateGroupProfile(null, new string('x', 513), null, Now).Error);

        // Nulls leave fields untouched.
        Assert.True(chat.UpdateGroupProfile(null, null, null, Now).IsSuccess);
        Assert.Equal("New name", chat.Title);
    }

    [Fact]
    public void CanInvite_admins_always_members_only_when_enabled()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;
        Assert.True(chat.CanInvite("u1"));
        Assert.False(chat.CanInvite("u2"));

        Assert.True(chat.UpdateGroupProfile(null, null, true, Now).IsSuccess);
        Assert.True(chat.CanInvite("u2"));

        var channel = Chat.CreateChannel("ch", "u1", "News", null, ["u2"], Now).Value!;
        Assert.True(channel.CanInvite("u1"));
        Assert.False(channel.CanInvite("u2"));

        var dm = Chat.CreateDm("d1", "u1", "u2", Now).Value!;
        Assert.False(dm.CanInvite("u1"));
    }

    [Fact]
    public void Mute_archive_and_unread_are_caller_relative()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;
        chat.ApplyNewMessage(1, "m1", "hello", "u1", Now);
        chat.MarkRead("u2", 1, Now);

        Assert.True(chat.SetMemberMute("u2", true, Now).IsSuccess);
        Assert.True(chat.GetMember("u2")!.IsMuted);
        Assert.False(chat.GetMember("u1")!.IsMuted);

        Assert.True(chat.SetMemberArchived("u2", true, Now).IsSuccess);
        Assert.True(chat.GetMember("u2")!.IsArchived);

        Assert.True(chat.MarkUnread("u2", 1, Now).IsSuccess);
        Assert.Equal(0, chat.GetMember("u2")!.LastReadSeq);
        Assert.Equal(1, chat.GetMember("u2")!.UnreadCount);
        // The other member is untouched.
        Assert.Equal(1, chat.GetMember("u1")!.LastReadSeq);
    }

    [Fact]
    public void SetMemberRole_transfers_nominal_ownership()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;
        Assert.Equal("u1", chat.OwnerId);

        Assert.True(chat.SetMemberRole("u2", ChatRole.Owner, Now).IsSuccess);
        Assert.True(chat.SetMemberRole("u1", ChatRole.Admin, Now).IsSuccess);
        Assert.Equal("u2", chat.OwnerId);
    }

    [Fact]
    public void Subscribers_can_react_but_not_post()
    {
        var chat = Chat.CreateChannel("c1", "u1", "News", null, ["u2"], Now).Value!;
        Assert.False(chat.CanPost("u2"));
        Assert.True(chat.CanReact("u2"));
        Assert.False(chat.CanReact("ghost"));
    }

    [Fact]
    public void MarkDelivered_advances_monotonically()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;
        chat.ApplyNewMessage(1, "m1", "hello", "u1", Now);
        chat.ApplyNewMessage(2, "m2", "again", "u1", Now);

        // Sender already holds the watermark from ApplyNewMessage.
        Assert.Equal(2, chat.GetMember("u1")!.LastDeliveredSeq);

        Assert.True(chat.MarkDelivered("u2", 1, Now));
        Assert.Equal(1, chat.GetMember("u2")!.LastDeliveredSeq);

        // Stale, duplicate, unknown-seq, and outsider acks are ignored.
        Assert.False(chat.MarkDelivered("u2", 1, Now));
        Assert.False(chat.MarkDelivered("u2", 0, Now));
        Assert.False(chat.MarkDelivered("u2", 99, Now));
        Assert.False(chat.MarkDelivered("ghost", 2, Now));

        Assert.True(chat.MarkDelivered("u2", 2, Now));
        Assert.Equal(2, chat.GetMember("u2")!.LastDeliveredSeq);
    }

    [Fact]
    public void MarkRead_implies_delivery()
    {
        var chat = Chat.CreateGroup("c1", "u1", "Team", ["u2"], null, Now).Value!;
        chat.ApplyNewMessage(1, "m1", "hello", "u1", Now);

        Assert.True(chat.MarkRead("u2", 1, Now));
        Assert.Equal(1, chat.GetMember("u2")!.LastDeliveredSeq);
    }
}
