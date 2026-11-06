using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.System;

/// <summary>
/// Foundation-only query: proves MediatR + Validation + CancellationToken wiring
/// end to end. Not messaging business logic; real feature slices land in Phase 1+.
/// </summary>
public sealed record GetSystemInfoQuery(string? Locale) : IRequest<Result<SystemInfoDto>>;

/// <summary>Deployment info for the frontend status pill and diagnostics.</summary>
public sealed record SystemInfoDto(
    string Service,
    string Version,
    DateTime ServerTimeUtc,
    string? Locale);

public sealed class GetSystemInfoQueryValidator : AbstractValidator<GetSystemInfoQuery>
{
    private static readonly string[] SupportedLocales = ["en", "fa"];

    public GetSystemInfoQueryValidator()
    {
        RuleFor(x => x.Locale)
            .Must(l => l is null || SupportedLocales.Contains(l.ToLowerInvariant()))
            .WithMessage("Locale must be one of: en, fa.");
    }
}

public sealed class GetSystemInfoQueryHandler(IClock clock)
    : IRequestHandler<GetSystemInfoQuery, Result<SystemInfoDto>>
{
    public Task<Result<SystemInfoDto>> Handle(GetSystemInfoQuery request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var version = typeof(GetSystemInfoQueryHandler).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var dto = new SystemInfoDto(
            Service: "Messenger.Api",
            Version: version,
            ServerTimeUtc: clock.UtcNow,
            Locale: request.Locale?.ToLowerInvariant());

        return Task.FromResult(Result<SystemInfoDto>.Ok(dto));
    }
}
