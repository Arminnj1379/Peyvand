namespace Messenger.Domain.Messages;

/// <summary>Message kind. Reply/forward carry metadata, not separate pipelines.</summary>
public enum MessageKind
{
    Text = 1,
    Reply = 2,
    Forward = 3,
    System = 4
}
