namespace Messenger.Domain.Common;

/// <summary>
/// Machine-readable domain/application failure.
/// Never carries message bodies or other PII.
/// </summary>
public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new("none", "No error.");
    public static readonly Error Unknown = new("unknown", "An unknown error occurred.");
}
