using FluentValidation;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Revocable invite links for groups and channels (DMs have none).
/// Access control: any member may view the active link so it can be shared;
/// only admins may rotate or revoke it; anyone holding a valid link may join
/// (groups as Member, channels as Subscriber).
/// </summary>
public sealed record GetInviteQuery(string ChatId);

public sealed record RotateInviteCommand(string ChatId);

public sealed record RevokeInviteCommand(string ChatId);

public sealed record JoinViaInviteCommand(string Code);

public sealed class GetInviteValidator : AbstractValidator<GetInviteQuery>
{
    public GetInviteValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class RotateInviteValidator : AbstractValidator<RotateInviteCommand>
{
    public RotateInviteValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class RevokeInviteValidator : AbstractValidator<RevokeInviteCommand>
{
    public RevokeInviteValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class JoinViaInviteValidator : AbstractValidator<JoinViaInviteCommand>
{
    public JoinViaInviteValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}
