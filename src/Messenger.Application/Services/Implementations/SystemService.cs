using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Features.System;
using Messenger.Application.Services.Interfaces;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Implementations;

/// <summary>Deployment info. Logic moved verbatim from the former MediatR handler.</summary>
public sealed class SystemService(IClock clock) : ISystemService
{
    public Task<Result<SystemInfoDto>> GetSystemInfoAsync(GetSystemInfoQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var validation = new GetSystemInfoQueryValidator().Validate(query);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        var version = typeof(GetSystemInfoQuery).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var dto = new SystemInfoDto(
            Service: "Messenger.Api",
            Version: version,
            ServerTimeUtc: clock.UtcNow,
            Locale: query.Locale?.ToLowerInvariant());

        return Task.FromResult(Result<SystemInfoDto>.Ok(dto));
    }
}
