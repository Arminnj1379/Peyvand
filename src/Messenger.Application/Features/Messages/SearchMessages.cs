using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Messaging;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Messages;

/// <summary>
/// Global message search across the caller's conversations (MongoDB text
/// index on bodies). Results are newest-first; deleted messages are excluded
/// (their bodies are wiped). Membership is enforced by scoping to chat ids.
/// </summary>
public sealed record SearchMessagesQuery(string Query, int Limit) : IRequest<Result<IReadOnlyList<MessageDto>>>;

public sealed class SearchMessagesValidator : AbstractValidator<SearchMessagesQuery>
{
    public SearchMessagesValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}

public sealed class SearchMessagesHandler(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<SearchMessagesQuery, Result<IReadOnlyList<MessageDto>>>
{
    public async Task<Result<IReadOnlyList<MessageDto>>> Handle(SearchMessagesQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var mine = await chats.ListForUserAsync(currentUser.UserId, 200, ct).ConfigureAwait(false);
        var chatIds = mine.Where(c => !c.IsDeleted).Select(c => c.Id).ToList();
        if (chatIds.Count == 0)
        {
            return Result<IReadOnlyList<MessageDto>>.Ok([]);
        }

        var found = await messages.SearchAsync(chatIds, request.Query.Trim(), request.Limit, ct)
            .ConfigureAwait(false);
        var visible = found.Where(m => !m.IsDeleted).ToList();
        var dtos = await MessageQueries.EnrichManyAsync(messages, files, users, visible, ct, currentUser.UserId)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<MessageDto>>.Ok(dtos);
    }
}
