using FluentValidation;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Group and channel profile management. All mutations are admin-only;
/// the domain rejects cross-type misuse (e.g. group settings on a channel).
/// </summary>
public sealed record UpdateGroupCommand(
    string ChatId,
    string? Title,
    string? Description,
    bool? MembersCanInvite);

public sealed record UpdateChannelCommand(
    string ChatId,
    string? Title,
    string? Description);

public sealed record SetChannelVisibilityCommand(
    string ChatId,
    bool IsPublic,
    string? Username);

public sealed class UpdateGroupValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Title).MinimumLength(1).MaximumLength(64)
            .When(x => x.Title is not null);
        RuleFor(x => x.Description).MaximumLength(512)
            .When(x => x.Description is not null);
    }
}

public sealed class UpdateChannelValidator : AbstractValidator<UpdateChannelCommand>
{
    public UpdateChannelValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Title).MinimumLength(1).MaximumLength(64)
            .When(x => x.Title is not null);
        RuleFor(x => x.Description).MaximumLength(512)
            .When(x => x.Description is not null);
    }
}

public sealed class SetChannelVisibilityValidator : AbstractValidator<SetChannelVisibilityCommand>
{
    public SetChannelVisibilityValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Username).MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.Username));
    }
}
