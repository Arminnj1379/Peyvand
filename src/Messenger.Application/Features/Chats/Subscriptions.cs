using FluentValidation;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Public channel subscriptions. Anyone may subscribe to a public channel;
/// private channels require an invite link or an admin add. Unsubscribing
/// follows the same sole-owner guard as leaving.
/// </summary>
public sealed record SubscribeChannelCommand(string ChatId);

public sealed record UnsubscribeChannelCommand(string ChatId);

public sealed record GetPublicChannelQuery(string Username);

public sealed class SubscribeChannelValidator : AbstractValidator<SubscribeChannelCommand>
{
    public SubscribeChannelValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class UnsubscribeChannelValidator : AbstractValidator<UnsubscribeChannelCommand>
{
    public UnsubscribeChannelValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetPublicChannelValidator : AbstractValidator<GetPublicChannelQuery>
{
    public GetPublicChannelValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(32);
    }
}
