namespace Messenger.Domain.Chats;

/// <summary>Conversation type. One Mongo collection, <c>type</c> discriminator.</summary>
public enum ChatType
{
    Dm = 1,
    Group = 2,
    Channel = 3
}
