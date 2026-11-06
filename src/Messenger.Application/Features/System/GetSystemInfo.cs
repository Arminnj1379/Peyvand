using FluentValidation;

namespace Messenger.Application.Features.System;

/// <summary>Deployment info for the frontend status pill and diagnostics.</summary>
public sealed record GetSystemInfoQuery(string? Locale);

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
