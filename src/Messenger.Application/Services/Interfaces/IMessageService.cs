using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Message send/history/search/edit/delete/forward/reactions.</summary>
public interface IMessageService
{
    Task<Result<MessageDto>> SendMessageAsync(SendMessageCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<HistoryPageDto>> GetHistoryAsync(GetHistoryQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<MessageDto>>> SyncMessagesAsync(
        SyncMessagesQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<MessageDto>>> ListPinnedAsync(
        ListPinnedQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<MessageDto>>> SearchMessagesAsync(
        SearchMessagesQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<MessageDto>> EditMessageAsync(EditMessageCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<MessageDto>> DeleteMessageAsync(DeleteMessageCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<MessageDto>> ForwardMessageAsync(
        ForwardMessageCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<MessageDto>> ToggleReactionAsync(
        ToggleReactionCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<MessageDto>>> GetRepliesAsync(
        GetRepliesQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<MessageDto>>> BulkDeleteMessagesAsync(
        BulkDeleteMessagesCommand command, CallerContext caller, CancellationToken ct);
}
