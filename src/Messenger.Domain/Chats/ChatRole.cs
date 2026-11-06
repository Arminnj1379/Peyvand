namespace Messenger.Domain.Chats;

/// <summary>Membership role. Subscribers are channel-only (read, no post).</summary>
public enum ChatRole
{
    Owner = 1,
    Admin = 2,
    Member = 3,
    Subscriber = 4
}
