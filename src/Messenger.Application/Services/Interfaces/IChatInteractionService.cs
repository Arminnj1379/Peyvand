using Messenger.Application.Features.Chats;
using Messenger.Application.Messaging;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>
/// Member-level conversation interaction: mute/archive/unread markers,
/// pinning, and read watermarks.
/// </summary>
public interface IChatInteractionService
{
    Task<Result<ChatDto>> SetMuteAsync(SetMuteCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> SetArchiveAsync(SetArchiveCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> MarkUnreadAsync(MarkUnreadCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> PinMessageAsync(PinMessageCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> UnpinMessageAsync(UnpinMessageCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> MarkReadAsync(MarkReadCommand command, CallerContext caller, CancellationToken ct);
}
