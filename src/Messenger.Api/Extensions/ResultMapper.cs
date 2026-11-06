using Messenger.Domain.Common;

namespace Messenger.Api.Extensions;

/// <summary>Maps domain/application errors to HTTP status codes (RFC 9457 problem details).</summary>
public static class ResultMapper
{
    public static IResult ToHttp<T>(Result<T> result, int successCode = 200)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess
            ? Results.Json(result.Value, statusCode: successCode)
            : Problem(result.Error);
    }

    public static IResult ToHttp(Result result, int successCode = 200)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess
            ? Results.StatusCode(successCode)
            : Problem(result.Error);
    }

    public static IResult Problem(Error error) =>
        Results.Problem(title: error.Message, statusCode: StatusFor(error.Code),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });

    public static int StatusFor(string code) => code switch
    {
        "call.livekit_not_configured" => 503,
        "file.too_large" => 413,
        "file.scan_rejected" => 422,
        "file.storage_unavailable" => 503,
        "auth.invalid_credentials" => 401,
        "auth.account_inactive" => 401,
        "auth.invalid_refresh_token" => 401,
        "auth.session_revoked" => 401,
        "auth.reuse_detected" => 401,
        "auth.unauthenticated" => 401,
        "auth.forbidden" => 403,
        "auth.cannot_grant_role" => 403,
        "chat.forbidden" => 403,
        "call.forbidden" => 403,
        "call.cannot_start" => 403,
        "call.not_initiator" => 403,
        "support.forbidden" => 403,
        "support.agent_only" => 403,
        "chat.cannot_post" => 403,
        "chat.not_admin" => 403,
        "chat.only_owner" => 403,
        "chat.invite_required" => 403,
        "chat.blocked" => 403,
        "file.not_owner" => 403,
        "message.not_sender" => 403,
        "message.cannot_delete" => 403,
        "message.cannot_react" => 403,
        "user.not_found" => 404,
        "session.not_found" => 404,
        "chat.not_found" => 404,
        "message.not_found" => 404,
        "call.not_found" => 404,
        "support.not_found" => 404,
        "file.not_found" => 404,
        "message.reply_target_not_found" => 404,
        "message.forward_source_not_found" => 404,
        "chat.not_member" => 404,
        "chat.not_subscribed" => 404,
        "chat.invite_invalid" => 404,
        "user.username_taken" => 409,
        "user.phone_taken" => 409,
        "user.role_conflict" => 409,
        "chat.already_member" => 409,
        "chat.already_pinned" => 409,
        "chat.already_subscribed" => 409,
        "chat.username_taken" => 409,
        "chat.pin_limit" => 409,
        "chat.member_limit" => 409,
        "message.client_id_conflict" => 409,
        "message.too_many_reactions" => 409,
        "file.already_attached" => 409,
        "call.already_active" => 409,
        "call.ended" => 409,
        "support.already_closed" => 409,
        _ => 400
    };
}
