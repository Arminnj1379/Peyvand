using System.Net;
using FluentValidation;
using Messenger.Application.Abstractions;

namespace Messenger.Api.Middleware;

/// <summary>
/// Centralized exception handling. Domain failures never reach here as
/// exceptions (they are <c>Result.Fail</c>); this handles infrastructure
/// faults, validation, and cancellation.
/// </summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — no response possible, no error log.
            logger.LogInformation("Request {Method} {Path} cancelled by client.", context.Request.Method, context.Request.Path);
        }
        catch (ValidationException ex)
        {
            logger.LogWarning("Validation failed for {Method} {Path}: {Errors}",
                context.Request.Method, context.Request.Path,
                string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)));
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "Validation failed.",
                status = 400,
                errors = ex.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()),
            }, context.RequestAborted).ConfigureAwait(false);
        }
        catch (DuplicateUserException ex)
        {
            // Lost a uniqueness race despite the pre-checks (username/phone taken).
            logger.LogWarning("Uniqueness conflict on {Method} {Path}: {Code}.",
                context.Request.Method, context.Request.Path, ex.Error.Code);
            context.Response.StatusCode = (int)HttpStatusCode.Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                title = ex.Error.Message,
                status = 409,
                code = ex.Error.Code,
            }, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Never include message bodies or stack traces in responses.
            logger.LogError(ex, "Unhandled error on {Method} {Path}.", context.Request.Method, context.Request.Path);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await context.Response.WriteAsJsonAsync(new
            {
                title = "An unexpected error occurred.",
                status = 500,
            }, context.RequestAborted).ConfigureAwait(false);
        }
    }
}
