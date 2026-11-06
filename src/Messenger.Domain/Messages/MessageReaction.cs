namespace Messenger.Domain.Messages;

/// <summary>One user's emoji reaction on a message.</summary>
public sealed record MessageReaction(string Emoji, string UserId, DateTime At);
