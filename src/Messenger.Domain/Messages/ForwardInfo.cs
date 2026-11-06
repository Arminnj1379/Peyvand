namespace Messenger.Domain.Messages;

/// <summary>Provenance of a forwarded message copy.</summary>
public sealed record ForwardInfo(string ChatId, string MessageId, string SenderId, DateTime SentAt);
