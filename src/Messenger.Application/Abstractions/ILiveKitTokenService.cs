namespace Messenger.Application.Abstractions;

/// <summary>What a client needs to connect media to the LiveKit SFU.</summary>
public sealed record LiveKitJoinInfo(string Url, string Token, string RoomName, string Identity);

/// <summary>
/// Server-side LiveKit token mint. The API secret never leaves the server:
/// clients receive short-lived per-call tokens only.
/// </summary>
public interface ILiveKitTokenService
{
    string ServerUrl { get; }
    bool IsConfigured { get; }
    string CreateJoinToken(string roomName, string identity, string displayName);
}
