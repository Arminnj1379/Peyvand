using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Messages;

namespace Messenger.Domain.Tests;

public sealed class MessageTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static Result<Message> Send(string id = "m1", string body = "hello", string clientId = "c-1") =>
        Message.Create(id, "chat1", 1, clientId, "u1", MessageKind.Text, body, null, null, Now);

    [Fact]
    public void Create_text_message_raises_sent_event()
    {
        var result = Send();

        Assert.True(result.IsSuccess);
        var message = result.Value!;
        Assert.Equal(1, message.Seq);
        Assert.False(message.IsEdited);
        var sent = Assert.Single(message.DomainEvents);
        Assert.IsType<MessageSent>(sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_empty_body(string body)
    {
        var result = Send(body: body);

        Assert.True(result.IsFailure);
        Assert.Equal(MessageErrors.InvalidBody, result.Error);
    }

    [Fact]
    public void Create_rejects_overlong_body()
    {
        var result = Send(body: new string('x', Message.MaxBodyLength + 1));

        Assert.True(result.IsFailure);
        Assert.Equal(MessageErrors.InvalidBody, result.Error);
    }

    [Fact]
    public void Reply_requires_target()
    {
        var result = Message.Create("m1", "chat1", 1, "c-1", "u1", MessageKind.Reply, "hi", null, null, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(MessageErrors.ReplyTargetNotFound, result.Error);
    }

    [Fact]
    public void Edit_marks_edited_with_timestamp()
    {
        var message = Send().Value!;

        Assert.True(message.Edit("updated", Now).IsSuccess);
        Assert.Equal("updated", message.Body);
        Assert.True(message.IsEdited);
        Assert.Equal(Now, message.EditedAt);
    }

    [Fact]
    public void Delete_wipes_body_and_blocks_second_delete()
    {
        var message = Send().Value!;

        Assert.True(message.Delete("u1", Now).IsSuccess);
        Assert.True(message.IsDeleted);
        Assert.Equal(string.Empty, message.Body);
        Assert.Equal("u1", message.DeletedBy);

        var again = message.Delete("u1", Now);
        Assert.True(again.IsFailure);
        Assert.Equal(MessageErrors.AlreadyDeleted, again.Error);
    }

    [Fact]
    public void Edit_after_delete_fails()
    {
        var message = Send().Value!;
        message.Delete("u1", Now);

        var edited = message.Edit("nope", Now);
        Assert.True(edited.IsFailure);
        Assert.Equal(MessageErrors.AlreadyDeleted, edited.Error);
    }

    [Fact]
    public void Preview_truncates_long_bodies()
    {
        var preview = Message.PreviewOf(new string('y', 200));

        Assert.Equal(141, preview.Length);
        Assert.EndsWith("…", preview);
        Assert.Equal("short", Message.PreviewOf("short"));
    }

    [Fact]
    public void Rehydrate_round_trips_state_without_events()
    {
        var message = Send().Value!;
        message.Edit("v2", Now);

        var revived = Message.Rehydrate(new MessageSnapshot(
            message.Id, message.ChatId, message.Seq, message.ClientMessageId,
            message.SenderId, message.Kind, message.Body, message.ReplyToMessageId,
            message.ForwardFrom, message.IsEdited, message.EditedAt,
            message.IsDeleted, message.DeletedAt, message.DeletedBy, message.CreatedAt));

        Assert.Equal("v2", revived.Body);
        Assert.True(revived.IsEdited);
        Assert.Empty(revived.DomainEvents);
    }

    [Fact]
    public void ToggleReaction_adds_then_removes()
    {
        var message = Send().Value!;

        var added = message.ToggleReaction("u2", "👍", Now);
        Assert.True(added.IsSuccess);
        Assert.True(added.Value);
        Assert.Single(message.Reactions);

        var removed = message.ToggleReaction("u2", "👍", Now);
        Assert.True(removed.IsSuccess);
        Assert.False(removed.Value);
        Assert.Empty(message.Reactions);
    }

    [Fact]
    public void ToggleReaction_groups_distinct_emoji()
    {
        var message = Send().Value!;
        message.ToggleReaction("u2", "👍", Now);
        message.ToggleReaction("u3", "👍", Now);
        message.ToggleReaction("u3", "❤️", Now);

        Assert.Equal(3, message.Reactions.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ToggleReaction_rejects_blank_emoji(string emoji)
    {
        var message = Send().Value!;
        var result = message.ToggleReaction("u2", emoji, Now);

        Assert.True(result.IsFailure);
        Assert.Equal(MessageErrors.ReactionInvalid, result.Error);
    }

    [Fact]
    public void ToggleReaction_rejected_on_deleted_message()
    {
        var message = Send().Value!;
        message.Delete("u1", Now);

        var result = message.ToggleReaction("u2", "👍", Now);
        Assert.True(result.IsFailure);
        Assert.Equal(MessageErrors.CannotReact, result.Error);
    }

    [Fact]
    public void Delete_wipes_reactions()
    {
        var message = Send().Value!;
        message.ToggleReaction("u2", "👍", Now);
        message.Delete("u1", Now);

        Assert.Empty(message.Reactions);
    }

    [Fact]
    public void Rehydrate_round_trips_reactions_and_reply_count()
    {
        var message = Send().Value!;
        message.ToggleReaction("u2", "👍", Now);
        message.BumpReplyCount();
        message.BumpReplyCount();

        var revived = Message.Rehydrate(new MessageSnapshot(
            message.Id, message.ChatId, message.Seq, message.ClientMessageId,
            message.SenderId, message.Kind, message.Body, message.ReplyToMessageId,
            message.ForwardFrom, message.IsEdited, message.EditedAt,
            message.IsDeleted, message.DeletedAt, message.DeletedBy, message.CreatedAt,
            message.Reactions.ToList(), message.ReplyCount));

        Assert.Equal(2, revived.ReplyCount);
        Assert.Single(revived.Reactions);
        Assert.Empty(revived.DomainEvents);
    }
}
