using Messenger.Domain.Common;

namespace Messenger.Domain.Chats;

/// <summary>Machine-readable chat/message failures. Messages are safe to surface.</summary>
public static class ChatErrors
{
    public static readonly Error NotFound =
        new("chat.not_found", "Conversation not found.");

    public static readonly Error Forbidden =
        new("chat.forbidden", "You are not a participant of this conversation.");

    public static readonly Error CannotPost =
        new("chat.cannot_post", "You cannot post in this conversation.");

    public static readonly Error CannotDmSelf =
        new("chat.cannot_dm_self", "You cannot start a conversation with yourself.");

    public static readonly Error DmExists =
        new("chat.dm_exists", "A direct conversation already exists.");

    public static readonly Error DmMembershipFixed =
        new("chat.dm_membership_fixed", "Direct conversation membership cannot be changed.");

    public static readonly Error DmRoleFixed =
        new("chat.dm_role_fixed", "Roles do not apply to direct conversations.");

    public static readonly Error AlreadyMember =
        new("chat.already_member", "This user is already a participant.");

    public static readonly Error NotMember =
        new("chat.not_member", "This user is not a participant.");

    public static readonly Error NotAdmin =
        new("chat.not_admin", "Only owners and admins can perform this action.");

    public static readonly Error SoleOwner =
        new("chat.sole_owner", "The sole owner cannot be removed or demoted.");

    public static readonly Error InvalidTitle =
        new("chat.invalid_title", "Title must be 1-64 characters.");

    public static readonly Error InvalidDescription =
        new("chat.invalid_description", "Description must be 512 characters or fewer.");

    public static readonly Error MemberLimit =
        new("chat.member_limit", "Conversation member limit reached.");

    public static readonly Error PinLimit =
        new("chat.pin_limit", "Pin limit reached (10). Unpin something first.");

    public static readonly Error AlreadyPinned =
        new("chat.already_pinned", "This message is already pinned.");

    public static readonly Error NotPinned =
        new("chat.not_pinned", "This message is not pinned.");

    public static readonly Error InvalidRole =
        new("chat.invalid_role", "This role cannot be assigned here.");

    public static readonly Error Deleted =
        new("chat.deleted", "This conversation is deleted.");

    public static readonly Error UsernameInvalid =
        new("chat.username_invalid", "Channel username must be 5-32 characters: lowercase letters, digits, '_'. Public channels only.");

    public static readonly Error UsernameTaken =
        new("chat.username_taken", "This channel username is already taken.");

    public static readonly Error NotPublic =
        new("chat.not_public", "This channel is private.");

    public static readonly Error InviteRequired =
        new("chat.invite_required", "This conversation is private. An invite link is required to join.");

    public static readonly Error InviteInvalid =
        new("chat.invite_invalid", "This invite link is invalid or has been revoked.");

    public static readonly Error AlreadySubscribed =
        new("chat.already_subscribed", "You are already subscribed to this channel.");

    public static readonly Error NotSubscribed =
        new("chat.not_subscribed", "You are not subscribed to this channel.");

    public static readonly Error Blocked =
        new("chat.blocked", "This conversation is not available.");

    public static readonly Error OnlyOwner =
        new("chat.only_owner", "Only the owner can perform this action.");

    public static readonly Error InvalidAvatar =
        new("chat.invalid_avatar", "Avatar must be a JPEG, PNG, or WebP image (2 MB or smaller).");
}

public static class MessageErrors
{
    public static readonly Error NotFound =
        new("message.not_found", "Message not found.");

    public static readonly Error InvalidBody =
        new("message.invalid_body", "Message text must be 1-4000 characters.");

    public static readonly Error InvalidClientMessageId =
        new("message.invalid_client_id", "Client message id is required.");

    public static readonly Error NotSender =
        new("message.not_sender", "Only the sender can edit this message.");

    public static readonly Error CannotDelete =
        new("message.cannot_delete", "You cannot delete this message.");

    public static readonly Error AlreadyDeleted =
        new("message.already_deleted", "This message is already deleted.");

    public static readonly Error ReplyTargetNotFound =
        new("message.reply_target_not_found", "The replied-to message was not found in this conversation.");

    public static readonly Error ForwardSourceNotFound =
        new("message.forward_source_not_found", "The forwarded message was not found.");

    public static readonly Error ClientIdConflict =
        new("message.client_id_conflict", "This client message id was already used in another conversation.");

    public static readonly Error ReactionInvalid =
        new("message.reaction_invalid", "This reaction is not supported.");

    public static readonly Error CannotReact =
        new("message.cannot_react", "You cannot react to this message.");

    public static readonly Error TooManyReactions =
        new("message.too_many_reactions", "This message already has the maximum number of reactions.");
}
