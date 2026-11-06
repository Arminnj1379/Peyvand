using Messenger.Domain.Chats;
using Messenger.Domain.Messages;
using Messenger.Infrastructure.Persistence.Mongo.Documents;

namespace Messenger.Infrastructure.Persistence.Mongo.Mapping;

/// <summary>Document ↔ aggregate translation for chats and messages.</summary>
internal static class ChatMapper
{
    public static ChatDocument ToDocument(Chat chat) => new()
    {
        Id = chat.Id,
        Type = chat.Type,
        Title = chat.Title,
        Description = chat.Description,
        OwnerId = chat.OwnerId,
        Members = chat.Members.Select(m => new ChatMemberDocument
        {
            UserId = m.UserId,
            Role = m.Role,
            JoinedAt = m.JoinedAt,
            LastReadSeq = m.LastReadSeq,
            LastReadAt = m.LastReadAt,
            UnreadCount = m.UnreadCount,
            IsMuted = m.IsMuted,
            IsArchived = m.IsArchived,
            LastDeliveredSeq = m.LastDeliveredSeq
        }).ToList(),
        MemberCount = chat.MemberCount,
        PinnedMessageIds = chat.PinnedMessageIds.ToList(),
        LastMessageId = chat.LastMessageId,
        LastMessageAt = chat.LastMessageAt,
        LastMessagePreview = chat.LastMessagePreview,
        SeqCounter = chat.SeqCounter,
        IsDeleted = chat.IsDeleted,
        IsPublic = chat.IsPublic,
        Username = chat.Username,
        InviteCode = chat.InviteCode,
        AvatarObjectKey = chat.AvatarObjectKey,
        AvatarUrl = chat.AvatarUrl,
        MembersCanInvite = chat.MembersCanInvite,
        DmKey = chat.DmKey,
        CreatedBy = chat.CreatedBy,
        CreatedAt = chat.CreatedAt,
        UpdatedAt = chat.UpdatedAt
    };

    public static Chat ToDomain(ChatDocument doc) => Chat.Rehydrate(new ChatSnapshot(
        doc.Id,
        doc.Type,
        doc.Title,
        doc.Description,
        doc.OwnerId,
        doc.Members.Select(m => new ChatMember(
            m.UserId, m.Role, m.JoinedAt, m.LastReadSeq, m.LastReadAt, m.UnreadCount, m.IsMuted, m.IsArchived,
            m.LastDeliveredSeq)).ToList(),
        doc.MemberCount,
        doc.PinnedMessageIds,
        doc.LastMessageId,
        doc.LastMessageAt,
        doc.LastMessagePreview,
        doc.SeqCounter,
        doc.IsDeleted,
        doc.CreatedBy,
        doc.CreatedAt,
        doc.UpdatedAt,
        doc.IsPublic,
        doc.Username,
        doc.InviteCode,
        doc.AvatarObjectKey,
        doc.AvatarUrl,
        doc.MembersCanInvite,
        doc.DmKey));
}

internal static class MessageMapper
{
    public static MessageDocument ToDocument(Message message) => new()
    {
        Id = message.Id,
        ChatId = message.ChatId,
        Seq = message.Seq,
        ClientMessageId = message.ClientMessageId,
        SenderId = message.SenderId,
        Kind = message.Kind,
        Body = message.Body,
        ReplyToMessageId = message.ReplyToMessageId,
        FileId = message.FileId,
        ForwardFrom = message.ForwardFrom is null
            ? null
            : new ForwardInfoDocument
            {
                ChatId = message.ForwardFrom.ChatId,
                MessageId = message.ForwardFrom.MessageId,
                SenderId = message.ForwardFrom.SenderId,
                SentAt = message.ForwardFrom.SentAt
            },
        IsEdited = message.IsEdited,
        EditedAt = message.EditedAt,
        IsDeleted = message.IsDeleted,
        DeletedAt = message.DeletedAt,
        DeletedBy = message.DeletedBy,
        Reactions = message.Reactions.Select(r => new MessageReactionDocument
        {
            Emoji = r.Emoji,
            UserId = r.UserId,
            At = r.At
        }).ToList(),
        ReplyCount = message.ReplyCount,
        CreatedAt = message.CreatedAt
    };

    public static Message ToDomain(MessageDocument doc) => Message.Rehydrate(new MessageSnapshot(
        doc.Id,
        doc.ChatId,
        doc.Seq,
        doc.ClientMessageId,
        doc.SenderId,
        doc.Kind,
        doc.Body,
        doc.ReplyToMessageId,
        doc.ForwardFrom is null
            ? null
            : new ForwardInfo(doc.ForwardFrom.ChatId, doc.ForwardFrom.MessageId, doc.ForwardFrom.SenderId, doc.ForwardFrom.SentAt),
        doc.IsEdited,
        doc.EditedAt,
        doc.IsDeleted,
        doc.DeletedAt,
        doc.DeletedBy,
        doc.CreatedAt,
        doc.Reactions.Select(r => new MessageReaction(r.Emoji, r.UserId, r.At)).ToList(),
        doc.ReplyCount,
        doc.FileId));
}
