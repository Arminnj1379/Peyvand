using Messenger.Domain.Users;

namespace Messenger.Application.Abstractions;

/// <summary>User aggregate store. One method per use-case need — no generics.</summary>
public interface IUserStore
{
    Task<User?> GetByIdAsync(string id, CancellationToken ct);
    /// <summary>
    /// Bulk fetch for enrichment paths (chat lists, history pages). Malformed
    /// ids are skipped; callers treat missing entries as unknown/deleted.
    /// One round-trip regardless of batch size (bounded by the caller).
    /// </summary>
    Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct);    Task<User?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<User?> GetByPhoneAsync(string phone, CancellationToken ct);
    Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct);
    Task<bool> ExistsByPhoneAsync(string phone, CancellationToken ct);
    Task<bool> AnyAdministratorAsync(CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task UpdateAsync(User user, CancellationToken ct);

    /// <summary>
    /// Directory search. <paramref name="includeCustomers"/> is decided by the
    /// caller (employees see everyone; customers see employees only).
    /// </summary>
    Task<IReadOnlyList<User>> SearchAsync(string query, bool includeCustomers, int limit, CancellationToken ct);

    Task<IReadOnlyList<User>> ListAsync(
        string? userType, string? role, bool? isActive, string? cursor, int limit, CancellationToken ct);
}
