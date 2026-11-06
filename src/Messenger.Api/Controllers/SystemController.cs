using Messenger.Api.Extensions;
using Messenger.Application.Features.System;
using Messenger.Application.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

/// <summary>Service info + server time. Proves API → Application wiring.</summary>
[ApiController]
[Route("api/v1/system")]
public sealed class SystemController(ISystemService system) : ControllerBase
{
    [HttpGet("info")]
    [ProducesResponseType(typeof(SystemInfoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IResult> GetSystemInfo([FromQuery] string? locale, CancellationToken ct)
    {
        var result = await system.GetSystemInfoAsync(new GetSystemInfoQuery(locale), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(title: result.Error.Message, statusCode: 500);
    }
}
