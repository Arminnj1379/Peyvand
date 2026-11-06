using System.ComponentModel.DataAnnotations;

namespace Messenger.Infrastructure.Calls;

/// <summary>
/// LiveKit SFU settings. Bound from the <c>LiveKit</c> config section.
/// The API secret never leaves the server: clients get short-lived JWTs only.
/// Empty keys = calls disabled (join returns 503, signaling still works).
/// </summary>
public sealed class LiveKitOptions
{
    public const string SectionName = "LiveKit";

    /// <summary>Public WebSocket URL clients connect to (e.g. wss://livekit.local).</summary>
    public string Url { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string ApiSecret { get; set; } = string.Empty;
}
