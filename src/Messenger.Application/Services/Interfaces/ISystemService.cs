using Messenger.Application.Features.System;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Deployment info for the frontend status pill and diagnostics.</summary>
public interface ISystemService
{
    Task<Result<SystemInfoDto>> GetSystemInfoAsync(GetSystemInfoQuery query, CancellationToken ct);
}
