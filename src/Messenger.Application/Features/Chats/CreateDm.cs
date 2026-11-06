using FluentValidation;
using Messenger.Domain.Chats;

namespace Messenger.Application.Features.Chats;

/// <summary>Direct conversation. Idempotent: same pair returns the existing DM.</summary>
public sealed record CreateDmCommand(string OtherUserId);

public sealed class CreateDmValidator : AbstractValidator<CreateDmCommand>
{
    public CreateDmValidator()
    {
        RuleFor(x => x.OtherUserId).NotEmpty().MaximumLength(64);
    }
}
