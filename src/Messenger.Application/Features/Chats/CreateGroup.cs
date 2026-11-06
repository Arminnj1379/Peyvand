using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Chats;

/// <summary>Group conversation (3..500 members). Creator becomes owner.</summary>
public sealed record CreateGroupCommand(
    string Title,
    IReadOnlyList<string> MemberIds,
    string? Description) : IRequest<Result<ChatDto>>;

public sealed class CreateGroupValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MinimumLength(1).MaximumLength(64);
        RuleFor(x => x.Description).MaximumLength(512);
        RuleFor(x => x.MemberIds).NotNull().Must(m => m.Count <= Chat.MaxMembers)
            .WithMessage($"Group is limited to {Chat.MaxMembers} members.");
    }
}

public sealed class CreateGroupHandler(
    IChatStore chats,
    IUserStore users,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<CreateGroupCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(CreateGroupCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = currentUser.UserId;
        var distinct = request.MemberIds
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToList();
        if (!distinct.Contains(callerId))
        {
            distinct.Insert(0, callerId);
        }

        // Every member must be a real, active user (ids come from the picker, never trusted blindly).
        var members = await users.ListByIdsAsync(distinct, ct).ConfigureAwait(false);
        var byId = members.ToDictionary(u => u.Id);
        foreach (var memberId in distinct)
        {
            if (!byId.TryGetValue(memberId, out var user) || !user.IsActive)
            {
                return Result<ChatDto>.Fail(UserErrors.NotFound);
            }
        }

        var now = clock.UtcNow;
        var created = Chat.CreateGroup(ids.NewId(), callerId, request.Title, distinct, request.Description, now);
        if (created.IsFailure)
        {
            return Result<ChatDto>.Fail(created.Error);
        }

        await chats.AddAsync(created.Value!, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, created.Value!, callerId, ct).ConfigureAwait(false));
    }
}
