namespace Messenger.Application.Abstractions;

/// <summary>UTC clock abstraction (testability seam).</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}
