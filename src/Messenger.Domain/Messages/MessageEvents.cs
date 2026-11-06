using Messenger.Domain.Common;

namespace Messenger.Domain.Messages;

/// <summary>Raised after a message is persisted (fan-out, push). Delivery-only signal.</summary>
public sealed record MessageSent(string ChatId, string MessageId, long Seq, string? SenderId, DateTime At)
    : IDomainEvent
{
    public DateTime OccurredAt => At;
}

public sealed record MessageEdited(string ChatId, string MessageId, DateTime At)
    : IDomainEvent
{
    public DateTime OccurredAt => At;
}

public sealed record MessageDeleted(string ChatId, string MessageId, string DeletedBy, DateTime At)
    : IDomainEvent
{
    public DateTime OccurredAt => At;
}

public sealed record MessageReacted(
    string ChatId, string MessageId, string UserId, string Emoji, bool Added, DateTime At)
    : IDomainEvent
{
    public DateTime OccurredAt => At;
}
