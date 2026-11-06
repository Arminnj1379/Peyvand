using Messenger.Domain.Files;

namespace Messenger.Application.Abstractions;

/// <summary>File metadata store (collection <c>files</c>). Binaries live in object storage.</summary>
public interface IFileStore
{
    Task<ChatFile?> GetByIdAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<ChatFile>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct);
    Task AddAsync(ChatFile file, CancellationToken ct);
    Task UpdateAsync(ChatFile file, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
}
