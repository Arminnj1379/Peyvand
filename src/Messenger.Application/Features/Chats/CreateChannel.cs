using FluentValidation;
using Messenger.Domain.Chats;

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
    string? Username = null);

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
