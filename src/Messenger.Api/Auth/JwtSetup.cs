using System.Security.Claims;
using System.Text;
using Messenger.Infrastructure.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Messenger.Api.Auth;

public static class JwtSetup
{
    public const string AdminPolicy = "Admin";
    public const string SupportPolicy = "Support";

    /// <summary>
    /// JWT bearer auth + role policies. Role names match <c>Messenger.Domain.Users.Role</c>.
    /// Also accepts the token via <c>?access_token=</c> for SignalR WebSocket upgrades.
    ///
    /// Validation parameters are built from <see cref="JwtOptions"/> at resolve
    /// time (not registration time) so late configuration sources — test hosts,
    /// reloadable providers — are honored by both minting and validation.
    /// </summary>
    public static IServiceCollection AddMessengerAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // SignalR browsers cannot set headers on WebSocket upgrades.
                        if (context.Request.Path.StartsWithSegments("/hubs/messenger"))
                        {
                            var token = context.Request.Query["access_token"].ToString();
                            if (!string.IsNullOrEmpty(token))
                            {
                                context.Token = token;
                            }
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                var options = jwt.Value;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = options.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    RoleClaimType = ClaimTypes.Role,
                    NameClaimType = "username"
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole("SystemAdministrator", "OrganizationAdministrator"))
            .AddPolicy(SupportPolicy, policy => policy.RequireRole(
                "SystemAdministrator", "OrganizationAdministrator", "SupportAgent"));

        return services;
    }
}
