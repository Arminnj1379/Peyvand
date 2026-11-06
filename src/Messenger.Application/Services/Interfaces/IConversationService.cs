using Messenger.Application.Features.Chats;
using Messenger.Application.Messaging;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>
/// Conversation lifecycle and queries: DMs, groups, channels, profiles,
/// visibility, deletion. Group collaboration (members, invites, pins, …)
/// lives in <see cref="IGroupService"/> and <see cref="IChatInteractionService"/>.
/// </summary>
public interface IConversationService
{
    Task<Result<ChatDto>> CreateDmAsync(CreateDmCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> CreateGroupAsync(CreateGroupCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> CreateChannelAsync(CreateChannelCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<ChatDto>>> ListMyChatsAsync(ListMyChatsQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<ChatDto>>> SearchMyChatsAsync(SearchMyChatsQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> GetChatAsync(GetChatQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> UpdateGroupAsync(UpdateGroupCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> UpdateChannelAsync(UpdateChannelCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> SetChannelVisibilityAsync(
        SetChannelVisibilityCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> DeleteGroupAsync(DeleteGroupCommand command, CallerContext caller, CancellationToken ct);
}
