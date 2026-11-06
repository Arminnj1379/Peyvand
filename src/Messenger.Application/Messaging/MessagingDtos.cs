using Messenger.Domain.Chats;
using Messenger.Domain.Messages;

namespace Messenger.Application.Messaging;

/// <summary>Conversation projection. Unread/mute/archive fields are caller-relative.</summary>
public sealed record MemberDto(
    string UserId,
    string Username,
    string DisplayName,
    string Role,
    DateTime JoinedAt,
    long LastReadSeq,
    int UnreadCount,
    bool IsMuted,
    long LastDeliveredSeq);

public sealed record ChatDto(
    string Id,
    string Type,
    string? Title,
    string? Description,
    string OwnerId,
    IReadOnlyList<MemberDto> Members,
    int MemberCount,
    IReadOnlyList<string> PinnedMessageIds,
    string? LastMessageId,
    DateTime? LastMessageAt,
    string? LastMessagePreview,
    long SeqCounter,
    int UnreadCount,
    long LastReadSeq,
    DateTime CreatedAt,
    bool IsPublic,
    string? Username,
    string? InviteCode,
    string? AvatarUrl,
    bool MembersCanInvite,
    bool IsMuted,
    bool IsArchived);

/// <summary>Public channel card. Safe to serve to non-members (no membership, no invite).</summary>
public sealed record PublicChannelDto(
    string Id,
    string Title,
    string? Description,
    string Username,
    int MemberCount,
    string? AvatarUrl);

public sealed record ForwardInfoDto(string ChatId, string MessageId, string SenderId, DateTime SentAt);

public sealed record ReplyPreviewDto(string MessageId, string? SenderName, string Preview, bool IsDeleted);

public sealed record ReactionDto(string Emoji, int Count, bool Mine);

public sealed record AttachmentDto(
    string FileId,
    string FileName,
    string Kind,
    string ContentType,
    long SizeBytes,
    string Url);

public sealed record MessageDto(
    string Id,
    string ChatId,
    long Seq,
    string ClientMessageId,
    string? SenderId,
    string? SenderUsername,
    string? SenderName,
    string Kind,
    string Body,
    ReplyPreviewDto? ReplyTo,
    ForwardInfoDto? ForwardFrom,
    bool IsEdited,
    DateTime? EditedAt,
    bool IsDeleted,
    DateTime CreatedAt,
    IReadOnlyList<ReactionDto> Reactions,
    int ReplyCount,
    AttachmentDto? Attachment);

public sealed record HistoryPageDto(
    IReadOnlyList<MessageDto> Items,
    long? NextCursor,
    bool HasMore);

public static class MessagingMapping
{
    public static MemberDto ToMemberDto(ChatMember member, string username, string displayName) =>
        new(member.UserId, username, displayName, member.Role.ToString(),
            member.JoinedAt, member.LastReadSeq, member.UnreadCount, member.IsMuted,
            member.LastDeliveredSeq);

    public static ChatDto ToDto(Chat chat, IReadOnlyDictionary<string, (string Username, string DisplayName)> users, string callerId)
    {
        var members = chat.Members
            .Select(m => users.TryGetValue(m.UserId, out var u)
                ? ToMemberDto(m, u.Username, u.DisplayName)
                : ToMemberDto(m, "unknown", "Unknown"))
            .ToList();
        var self = chat.GetMember(callerId);
        return new ChatDto(
            chat.Id, chat.Type.ToString(), chat.Title, chat.Description, chat.OwnerId,
            members, chat.MemberCount, chat.PinnedMessageIds.ToList(),
            chat.LastMessageId, chat.LastMessageAt, chat.LastMessagePreview,
            chat.SeqCounter, self?.UnreadCount ?? 0, self?.LastReadSeq ?? 0,
            chat.CreatedAt, chat.IsPublic, chat.Username, chat.InviteCode, chat.AvatarUrl,
            chat.MembersCanInvite, self?.IsMuted ?? false, self?.IsArchived ?? false);
    }

    public static PublicChannelDto ToPublicDto(Chat chat) =>
        new(chat.Id, chat.Title ?? "Untitled", chat.Description,
            chat.Username ?? string.Empty, chat.MemberCount, chat.AvatarUrl);

    public static MessageDto ToDto(
        Message message,
        string? senderUsername,
        string? senderName,
        ReplyPreviewDto? reply = null,
        string? callerId = null,
        AttachmentDto? attachment = null) =>
        new(message.Id, message.ChatId, message.Seq, message.ClientMessageId,
            message.SenderId, senderUsername, senderName, message.Kind.ToString(),
            message.IsDeleted ? string.Empty : message.Body, reply,
            message.ForwardFrom is null
                ? null
                : new ForwardInfoDto(
                    message.ForwardFrom.ChatId, message.ForwardFrom.MessageId,
                    message.ForwardFrom.SenderId, message.ForwardFrom.SentAt),
            message.IsEdited, message.EditedAt, message.IsDeleted, message.CreatedAt,
            ToReactionDtos(message, callerId), message.ReplyCount,
            message.IsDeleted ? null : attachment);

    public static IReadOnlyList<ReactionDto> ToReactionDtos(Message message, string? callerId)
    {
        if (message.Reactions.Count == 0)
        {
            return [];
        }

        return message.Reactions
            .GroupBy(r => r.Emoji)
            .Select(g => new ReactionDto(
                g.Key, g.Count(), callerId is not null && g.Any(r => r.UserId == callerId)))
            .OrderByDescending(r => r.Count)
            .ToList();
    }
}
