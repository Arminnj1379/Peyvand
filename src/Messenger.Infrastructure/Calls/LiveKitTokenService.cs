using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Messenger.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Messenger.Infrastructure.Calls;

/// <summary>
/// Mints LiveKit room tokens as plain JWTs (same grant shape as the official
/// server SDK, no extra dependency). HS256 over the API secret; 6 h TTL.
/// </summary>
public sealed class LiveKitTokenService(IOptions<LiveKitOptions> options, IClock clock) : ILiveKitTokenService
{
    private readonly LiveKitOptions _options = options.Value;

    public string ServerUrl => _options.Url;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.Url) &&
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.ApiSecret);

    public string CreateJoinToken(string roomName, string identity, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomName);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (!IsConfigured)
        {
            throw new InvalidOperationException("LiveKit is not configured.");
        }

        var now = clock.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.ApiSecret));
        var grants = "{\"roomJoin\":true,\"room\":\"" + roomName +
            "\",\"canPublish\":true,\"canSubscribe\":true,\"canPublishData\":true}";

        var jwt = new JwtSecurityToken(
            issuer: _options.ApiKey,
            audience: null,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, identity),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("n")),
                new Claim("name", displayName),
                new Claim("video", grants, "json")
            ],
            notBefore: now,
            expires: now.AddHours(6),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
