using Messenger.Domain.Sessions;

namespace Messenger.Domain.Tests;

public sealed class DeviceSessionTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static DeviceSession Create(string hash = "h1") =>
        DeviceSession.Create("s1", "u1", hash, "web", "127.0.0.1", Now, Now.AddDays(30));

    [Fact]
    public void New_session_is_usable()
    {
        var session = Create();
        Assert.True(session.IsUsable(Now));
        Assert.False(session.IsRevoked);
        Assert.False(session.IsExpired(Now));
    }

    [Fact]
    public void Rotate_retires_previous_hash_for_reuse_detection()
    {
        var session = Create();
        session.Rotate("h2", Now.AddDays(30), null, Now);

        Assert.Equal("h2", session.RefreshTokenHash);
        Assert.True(session.IsRetired("h1"));
        Assert.False(session.IsRetired("h2"));
    }

    [Fact]
    public void Rotate_keeps_only_last_five_retired_hashes()
    {
        var session = Create("h0");
        for (var i = 1; i <= 7; i++)
        {
            session.Rotate($"h{i}", Now.AddDays(30), null, Now);
        }

        Assert.Equal(5, session.RetiredTokenHashes.Count);
        Assert.DoesNotContain("h0", session.RetiredTokenHashes);
        Assert.DoesNotContain("h1", session.RetiredTokenHashes);
        Assert.Contains("h2", session.RetiredTokenHashes);
    }

    [Fact]
    public void Expired_session_is_not_usable()
    {
        var session = Create();
        Assert.True(session.IsExpired(Now.AddDays(31)));
        Assert.False(session.IsUsable(Now.AddDays(31)));
    }

    [Fact]
    public void Revoke_is_idempotent_and_kills_usability()
    {
        var session = Create();
        session.Revoke(Now);
        var first = session.RevokedAt;
        session.Revoke(Now.AddHours(1));
        Assert.Equal(first, session.RevokedAt);
        Assert.True(session.IsRevoked);
        Assert.False(session.IsUsable(Now));
    }

    [Fact]
    public void Create_rejects_past_expiry()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DeviceSession.Create("s1", "u1", "h", null, null, Now, Now.AddSeconds(-1)));
    }

    [Fact]
    public void Rehydrate_round_trips_state()
    {
        var session = Create();
        session.Rotate("h2", Now.AddDays(30), "1.2.3.4", Now);

        var revived = DeviceSession.Rehydrate(new SessionSnapshot(
            session.Id, session.UserId, session.RefreshTokenHash,
            session.RetiredTokenHashes.ToList(), session.DeviceLabel, session.IpAddress,
            session.CreatedAt, session.LastUsedAt, session.ExpiresAt, session.RevokedAt));

        Assert.Equal("h2", revived.RefreshTokenHash);
        Assert.True(revived.IsRetired("h1"));
        Assert.Equal("1.2.3.4", revived.IpAddress);
    }
}
