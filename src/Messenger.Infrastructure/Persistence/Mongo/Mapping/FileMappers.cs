using Messenger.Domain.Files;
using Messenger.Infrastructure.Persistence.Mongo.Documents;

namespace Messenger.Infrastructure.Persistence.Mongo.Mapping;

/// <summary>Document ↔ aggregate translation for files.</summary>
internal static class FileMapper
{
    public static FileDocument ToDocument(ChatFile file) => new()
    {
        Id = file.Id,
        OwnerId = file.OwnerId,
        ChatId = file.ChatId,
        MessageId = file.MessageId,
        FileName = file.FileName,
        ContentType = file.ContentType,
        SizeBytes = file.SizeBytes,
        Sha256 = file.Sha256,
        Kind = file.Kind,
        Status = file.Status,
        ObjectKey = file.ObjectKey,
        CreatedAt = file.CreatedAt,
        AttachedAt = file.AttachedAt
    };

    public static ChatFile ToDomain(FileDocument doc) => ChatFile.Rehydrate(new FileSnapshot(
        doc.Id,
        doc.OwnerId,
        doc.ChatId,
        doc.MessageId,
        doc.FileName,
        doc.ContentType,
        doc.SizeBytes,
        doc.Sha256,
        doc.Kind,
        doc.Status,
        doc.ObjectKey,
        doc.CreatedAt,
        doc.AttachedAt));
}
