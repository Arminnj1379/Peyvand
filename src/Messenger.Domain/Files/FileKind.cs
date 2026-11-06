namespace Messenger.Domain.Files;

/// <summary>Attachment category. Drives size limits, preview rendering, and validation.</summary>
public enum FileKind
{
    Image = 1,
    Video = 2,
    Audio = 3,
    Document = 4
}
