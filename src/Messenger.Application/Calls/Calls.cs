using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Messaging;
using Messenger.Domain.Calls;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Calls;

/// <summary>
/// Call signaling. Every operation re-verifies chat membership
/// server-side; the JWT subject is the only identity. Media flows through
/// LiveKit — this slice only tracks who is invited/joined/left/ended.
/// </summary>
public sealed record StartCallCommand(string ChatId, string MediaType);

public sealed class StartCallValidator : AbstractValidator<StartCallCommand>
{
    public StartCallValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MediaType).NotEmpty().Must(v =>
            v.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Call type must be audio or video.");
    }
}

public sealed record GetCallQuery(string CallId);

public sealed class GetCallValidator : AbstractValidator<GetCallQuery>
{
    public GetCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed record ListCallsQuery(string ChatId, int Limit);

public sealed class ListCallsValidator : AbstractValidator<ListCallsQuery>
{
    public ListCallsValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed record AcceptCallCommand(string CallId);

public sealed class AcceptCallValidator : AbstractValidator<AcceptCallCommand>
{
    public AcceptCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed record RejectCallCommand(string CallId);

public sealed class RejectCallValidator : AbstractValidator<RejectCallCommand>
{
    public RejectCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed record JoinCallCommand(string CallId);

public sealed class JoinCallValidator : AbstractValidator<JoinCallCommand>
{
    public JoinCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed record LeaveCallCommand(string CallId);

public sealed class LeaveCallValidator : AbstractValidator<LeaveCallCommand>
{
    public LeaveCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed record EndCallCommand(string CallId);

public sealed class EndCallValidator : AbstractValidator<EndCallCommand>
{
    public EndCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}
