using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Messaging;

/// <summary>Single home of conversation authorization. Handlers never trust client ids.</summary>
internal static class ChatAccess
{
    public static string? RequireCaller(ICurrentUser caller) => caller.UserId;

    public static Result<Chat> RequireMember(Chat? chat, string callerId)
    {
        if (chat is null || chat.IsDeleted)
        {
            return Result<Chat>.Fail(ChatErrors.NotFound);
        }

        if (!chat.IsMember(callerId))
        {
            return Result<Chat>.Fail(ChatErrors.Forbidden);
        }

        return Result<Chat>.Ok(chat);
    }

    public static Result<Chat> RequireCanPost(Chat? chat, string callerId)
    {
        var member = RequireMember(chat, callerId);
        if (member.IsFailure)
        {
            return member;
        }

        if (!member.Value!.CanPost(callerId))
        {
            return Result<Chat>.Fail(ChatErrors.CannotPost);
        }

        return member;
    }

    public static Result<Chat> RequireAdmin(Chat? chat, string callerId)
    {
        var member = RequireMember(chat, callerId);
        if (member.IsFailure)
        {
            return member;
        }

        if (!member.Value!.IsAdmin(callerId))
        {
            return Result<Chat>.Fail(ChatErrors.NotAdmin);
        }

        return member;
    }

    public static Error Unauthenticated() => AuthErrors.Unauthenticated;
}
