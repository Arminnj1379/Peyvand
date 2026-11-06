using FluentValidation;
using Messenger.Domain.Chats;

namespace Messenger.Application.Features.Chats;

/// <summary>Group conversation (3..500 members). Creator becomes owner.</summary>
public sealed record CreateGroupCommand(
    string Title,
    IReadOnlyList<string> MemberIds,
    string? Description);

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
