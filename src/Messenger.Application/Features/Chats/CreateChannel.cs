using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Broadcast channel. Only owners/admins post; subscribers read and react.
/// Public channels carry a unique username and are joinable by anyone;
/// private channels need an invite link or an admin add.
/// </summary>
public sealed record CreateChannelCommand(
    string Title,
    string? Description,
    IReadOnlyList<string>? SubscriberIds,
    bool IsPublic = false,
    string? Username = null) : IRequest<Result<ChatDto>>;

public sealed class CreateChannelValidator : AbstractValidator<CreateChannelCommand>
{
    public CreateChannelValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(1).MaximumLength(64);
        RuleFor(x => x.Description).MaximumLength(512);
        RuleFor(x => x.Username).MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.Username));
    }
}

public sealed class CreateChannelHandler(
    IChatStore chats,
    IUserStore users,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<CreateChannelCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(CreateChannelCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = currentUser.UserId;
        var subs = (request.SubscriberIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        // One round-trip for the whole subscriber list (was: GetById per id).
        var found = await users.ListByIdsAsync(subs, ct).ConfigureAwait(false);
        var byId = found.ToDictionary(u => u.Id);
        foreach (var sub in subs)
        {
            if (!byId.TryGetValue(sub, out var user) || !user.IsActive)
            {
                return Result<ChatDto>.Fail(UserErrors.NotFound);
            }
        }

        if (request.IsPublic)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || !Chat.IsValidUsername(request.Username))
            {
                return Result<ChatDto>.Fail(ChatErrors.UsernameInvalid);
            }

            var taken = await chats.GetByUsernameAsync(request.Username, ct).ConfigureAwait(false);
            if (taken is not null)
            {
                return Result<ChatDto>.Fail(ChatErrors.UsernameTaken);
            }
        }

        var now = clock.UtcNow;
        var created = Chat.CreateChannel(
            ids.NewId(), callerId, request.Title, request.Description, subs, now,
            request.IsPublic, request.Username);
        if (created.IsFailure)
        {
            return Result<ChatDto>.Fail(created.Error);
        }

        try
        {
            await chats.AddAsync(created.Value!, ct).ConfigureAwait(false);
        }
        catch (DuplicateChatException)
        {
            // Lost a concurrent claim on the public username.
            return Result<ChatDto>.Fail(ChatErrors.UsernameTaken);
        }

        return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, created.Value!, callerId, ct).ConfigureAwait(false));
    }
}
