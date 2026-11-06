using FluentValidation;
using Messenger.Domain.Chats;

namespace Messenger.Application.Features.Chats;

/// <summary>Participant management. Groups/channels only; DMs are fixed pairs.</summary>
public sealed record AddMemberCommand(string ChatId, string UserId);

public sealed record RemoveMemberCommand(string ChatId, string UserId);

public sealed record LeaveChatCommand(string ChatId);

public sealed record SetMemberRoleCommand(string ChatId, string UserId, string Role);

public sealed class AddMemberValidator : AbstractValidator<AddMemberCommand>
{
    public AddMemberValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class RemoveMemberValidator : AbstractValidator<RemoveMemberCommand>
{
    public RemoveMemberValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class LeaveChatValidator : AbstractValidator<LeaveChatCommand>
{
    public LeaveChatValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class SetMemberRoleValidator : AbstractValidator<SetMemberRoleCommand>
{
    public SetMemberRoleValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Role).Must(r => Enum.TryParse<ChatRole>(r, ignoreCase: true, out _))
            .WithMessage("Unknown role: '{PropertyValue}'.");
    }
}
