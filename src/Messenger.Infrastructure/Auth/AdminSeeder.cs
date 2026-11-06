using Messenger.Application.Abstractions;
using Messenger.Domain.Common;
using Messenger.Domain.Users;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Auth;

/// <summary>
/// Creates the initial System Administrator on first boot when enabled.
/// Skips silently when an administrator already exists or when disabled.
/// </summary>
public sealed class AdminSeeder(
    IUserStore users,
    IPasswordHasher passwords,
    IIdGenerator ids,
    IClock clock,
    IOptions<SeedAdminOptions> options,
    ILogger<AdminSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.Enabled)
        {
            return;
        }

        if (await users.AnyAdministratorAsync(cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation("Administrator account already exists; seed skipped.");
            return;
        }

        if (string.IsNullOrWhiteSpace(config.Password) || config.Password.Length < 8)
        {
            throw new InvalidOperationException("SeedAdmin is enabled but Password is missing or too short.");
        }

        var now = clock.UtcNow;
        var registered = User.Register(
            ids.NewId(),
            User.NormalizeUsername(config.Username),
            config.DisplayName,
            passwords.Hash(config.Password),
            UserType.Employee,
            [Role.SystemAdministrator, Role.OrganizationAdministrator],
            phone: null,
            now);
        if (registered.IsFailure)
        {
            throw new InvalidOperationException($"Seed admin invalid: {registered.Error.Message}");
        }

        await users.AddAsync(registered.Value!, cancellationToken).ConfigureAwait(false);
        logger.LogWarning(
            "Seeded system administrator '{Username}'. Change its password immediately in non-dev environments.",
            config.Username);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
