using Messenger.Domain.Calls;

namespace Messenger.Application.Abstractions;

/// <summary>Call signaling store (collection <c>calls</c>). Media never touches this port.</summary>
public interface ICallStore
{
    Task<Call?> GetByIdAsync(string id, CancellationToken ct);
    Task<Call?> GetActiveForChatAsync(string chatId, CancellationToken ct);
    Task<IReadOnlyList<Call>> ListForChatAsync(string chatId, int limit, CancellationToken ct);
    Task AddAsync(Call call, CancellationToken ct);
    Task UpdateAsync(Call call, CancellationToken ct);
}
