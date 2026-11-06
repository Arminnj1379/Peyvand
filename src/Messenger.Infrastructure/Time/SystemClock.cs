using Messenger.Application.Abstractions;

namespace Messenger.Infrastructure.Time;

/// <summary>Production UTC clock.</summary>
public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
