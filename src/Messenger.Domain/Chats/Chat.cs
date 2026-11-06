using System.Text.RegularExpressions;
using Messenger.Domain.Common;

namespace Messenger.Domain.Chats;

/// <summary>
/// Conversation aggregate root. Membership is embedded (MVP cap 500); message
/// history always lives in the <c>messages</c> collection, never here.
/// Persistence-ignorant: no BSON, no ASP.NET Core.
///
/// Authorization summary (enforced here + in Application handlers):
/// <list type="bullet">
/// <item>DM: fixed pair, either member may post/pin; no roles, no invites.</item>
/// <item>Group: any member may post and react. Admins manage members, roles,
/// profile, pins, invites, and avatars. Members may invite only when
/// <see cref="MembersCanInvite"/> is on.</item>
/// <item>Channel: only Owner/Admin may post, pin, and manage. Subscribers read
/// and react. Public channels are joinable by anyone; private ones need an
/// invite code or an admin add.</item>
/// </list>
/// </summary>
public sealed class Chat : Entity
{
    public const int MaxMembers = 500;
    public const int MaxPins = 10;

    private static readonly Regex UsernamePattern = new("^[a-z0-9_]{5,32}$", RegexOptions.Compiled);

    private readonly List<ChatMember> _members = [];
    private readonly List<string> _pinnedMessageIds = [];

    private Chat(
        string id,
        ChatType type,
        string? title,
        string? description,
        string ownerId,
        DateTime now) : base(id)
    {
        Type = type;
        Title = title;
        Description = description;
        OwnerId = ownerId;
        CreatedBy = ownerId;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public ChatType Type { get; }
    public string? Title { get; private set; }
    public string? Description { get; private set; }
    public string OwnerId { get; private set; }
    public IReadOnlyList<ChatMember> Members => _members.AsReadOnly();
    public int MemberCount { get; private set; }
    public IReadOnlyList<string> PinnedMessageIds => _pinnedMessageIds.AsReadOnly();
    public string? LastMessageId { get; private set; }
    public DateTime? LastMessageAt { get; private set; }
    public string? LastMessagePreview { get; private set; }
    public long SeqCounter { get; private set; }
    public bool IsDeleted { get; private set; }
    public string CreatedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    /// <summary>Channels only: public channels are discoverable and joinable by anyone.</summary>
    public bool IsPublic { get; private set; }

    /// <summary>Public channel handle (lowercase, unique). Null for private/direct/group chats.</summary>
    public string? Username { get; private set; }

    /// <summary>Revocable opaque invite code. Null means no active invite link.</summary>
    public string? InviteCode { get; private set; }

    public string? AvatarObjectKey { get; private set; }
    public string? AvatarUrl { get; private set; }

    /// <summary>Groups only: when true, plain members may add new members.</summary>
    public bool MembersCanInvite { get; private set; }

    /// <summary>
    /// Deterministic DM identity: the two member ids sorted and joined.
    /// Backs the sparse unique <c>ux_dm_key</c> index so concurrent creates for
    /// the same pair collapse to one conversation instead of duplicating.
    /// Null for group/channel chats (and legacy DMs written before this field).
    /// </summary>
    public string? DmKey { get; private set; }

    public bool IsMember(string userId) => _members.Any(m => m.UserId == userId);

    public ChatMember? GetMember(string userId) => _members.FirstOrDefault(m => m.UserId == userId);

    public bool CanPost(string userId)
    {
        var member = GetMember(userId);
        if (member is null || IsDeleted)
        {
            return false;
        }

        // Channels broadcast: only owners/admins post, subscribers read.
        if (Type == ChatType.Channel)
        {
            return member.Role is ChatRole.Owner or ChatRole.Admin;
        }

        return true;
    }

    /// <summary>Reactions are lighter than posts: every member (incl. subscribers) may react.</summary>
    public bool CanReact(string userId)
    {
        var member = GetMember(userId);
        return member is not null && !IsDeleted;
    }

    public bool IsAdmin(string userId)
    {
        var member = GetMember(userId);
        return member?.Role is ChatRole.Owner or ChatRole.Admin;
    }

    /// <summary>Who may add members: admins always; members only when the group allows it.</summary>
    public bool CanInvite(string userId)
    {
        if (Type == ChatType.Dm || IsDeleted)
        {
            return false;
        }

        if (IsAdmin(userId))
        {
            return true;
        }

        var member = GetMember(userId);
        return member is not null && Type == ChatType.Group && MembersCanInvite;
    }

    public static Result<Chat> CreateDm(string id, string creatorId, string otherUserId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(creatorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(otherUserId);
        if (creatorId == otherUserId)
        {
            return Result<Chat>.Fail(ChatErrors.CannotDmSelf);
        }

        var chat = new Chat(id, ChatType.Dm, null, null, creatorId, now);
        chat._members.Add(new ChatMember(creatorId, ChatRole.Member, now, 0, null, 0, false, false));
        chat._members.Add(new ChatMember(otherUserId, ChatRole.Member, now, 0, null, 0, false, false));
        chat.MemberCount = 2;
        chat.DmKey = BuildDmKey(creatorId, otherUserId);
        return Result<Chat>.Ok(chat);
    }

    /// <summary>Order-independent pair key (ObjectIds are hex, so ':' never collides).</summary>
    public static string BuildDmKey(string userA, string userB)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userA);
        ArgumentException.ThrowIfNullOrWhiteSpace(userB);
        return string.CompareOrdinal(userA, userB) <= 0 ? $"{userA}:{userB}" : $"{userB}:{userA}";
    }

    public static Result<Chat> CreateGroup(
        string id, string creatorId, string title, IEnumerable<string> memberIds,
        string? description, DateTime now, bool membersCanInvite = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(creatorId);
        if (!IsValidTitle(title))
        {
            return Result<Chat>.Fail(ChatErrors.InvalidTitle);
        }

        if (description is not null && description.Length > 512)
        {
            return Result<Chat>.Fail(ChatErrors.InvalidDescription);
        }

        var distinct = memberIds.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        if (!distinct.Contains(creatorId))
        {
            distinct.Insert(0, creatorId);
        }

        if (distinct.Count < 2)
        {
            return Result<Chat>.Fail(ChatErrors.NotMember);
        }

        if (distinct.Count > MaxMembers)
        {
            return Result<Chat>.Fail(ChatErrors.MemberLimit);
        }

        var chat = new Chat(id, ChatType.Group, title.Trim(), description?.Trim(), creatorId, now);
        chat.MembersCanInvite = membersCanInvite;
        foreach (var memberId in distinct)
        {
            var role = memberId == creatorId ? ChatRole.Owner : ChatRole.Member;
            chat._members.Add(new ChatMember(memberId, role, now, 0, null, 0, false, false));
        }

        chat.MemberCount = chat._members.Count;
        return Result<Chat>.Ok(chat);
    }

    public static Result<Chat> CreateChannel(
        string id, string creatorId, string title, string? description,
        IEnumerable<string>? subscriberIds, DateTime now,
        bool isPublic = false, string? username = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(creatorId);
        if (!IsValidTitle(title))
        {
            return Result<Chat>.Fail(ChatErrors.InvalidTitle);
        }

        if (description is not null && description.Length > 512)
        {
            return Result<Chat>.Fail(ChatErrors.InvalidDescription);
        }

        string? normalizedUsername = null;
        if (isPublic)
        {
            if (string.IsNullOrWhiteSpace(username) || !IsValidUsername(username))
            {
                return Result<Chat>.Fail(ChatErrors.UsernameInvalid);
            }

            normalizedUsername = NormalizeUsername(username);
        }
        else if (!string.IsNullOrWhiteSpace(username))
        {
            return Result<Chat>.Fail(ChatErrors.UsernameInvalid);
        }

        var chat = new Chat(id, ChatType.Channel, title.Trim(), description?.Trim(), creatorId, now);
        chat.IsPublic = isPublic;
        chat.Username = normalizedUsername;
        chat._members.Add(new ChatMember(creatorId, ChatRole.Owner, now, 0, null, 0, false, false));
        foreach (var sub in (subscriberIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct())
        {
            if (sub == creatorId || chat._members.Count >= MaxMembers)
            {
                continue;
            }

            chat._members.Add(new ChatMember(sub, ChatRole.Subscriber, now, 0, null, 0, false, false));
        }

        chat.MemberCount = chat._members.Count;
        return Result<Chat>.Ok(chat);
    }

    public Result AddMember(string userId, ChatRole role, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (IsDeleted)
        {
            return Result.Fail(ChatErrors.Deleted);
        }

        if (Type == ChatType.Dm)
        {
            return Result.Fail(ChatErrors.DmMembershipFixed);
        }

        if (IsMember(userId))
        {
            return Result.Fail(ChatErrors.AlreadyMember);
        }

        if (_members.Count >= MaxMembers)
        {
            return Result.Fail(ChatErrors.MemberLimit);
        }

        if (!IsRoleAllowed(role))
        {
            return Result.Fail(ChatErrors.InvalidRole);
        }

        _members.Add(new ChatMember(userId, role, now, SeqCounter, now, 0, false, false));
        MemberCount = _members.Count;
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result RemoveMember(string userId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (Type == ChatType.Dm)
        {
            return Result.Fail(ChatErrors.DmMembershipFixed);
        }

        var member = GetMember(userId);
        if (member is null)
        {
            return Result.Fail(ChatErrors.NotMember);
        }

        if (member.Role == ChatRole.Owner && _members.Count(m => m.Role == ChatRole.Owner) <= 1)
        {
            return Result.Fail(ChatErrors.SoleOwner);
        }

        _members.Remove(member);
        MemberCount = _members.Count;
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result SetMemberRole(string userId, ChatRole role, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (Type == ChatType.Dm)
        {
            return Result.Fail(ChatErrors.DmRoleFixed);
        }

        var member = GetMember(userId);
        if (member is null)
        {
            return Result.Fail(ChatErrors.NotMember);
        }

        if (!IsRoleAllowed(role))
        {
            return Result.Fail(ChatErrors.InvalidRole);
        }

        if (member.Role == ChatRole.Owner && role != ChatRole.Owner &&
            _members.Count(m => m.Role == ChatRole.Owner) <= 1)
        {
            return Result.Fail(ChatErrors.SoleOwner);
        }

        _members.Remove(member);
        _members.Add(member with { Role = role });
        // Ownership follows the role: the newest Owner becomes the nominal owner
        // when the previous owner steps down.
        if (role == ChatRole.Owner)
        {
            OwnerId = userId;
        }
        else if (member.Role == ChatRole.Owner && OwnerId == userId)
        {
            OwnerId = _members.FirstOrDefault(m => m.Role == ChatRole.Owner)?.UserId ?? OwnerId;
        }

        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>Admin-only group profile edit. Null args leave the field unchanged.</summary>
    public Result UpdateGroupProfile(string? title, string? description, bool? membersCanInvite, DateTime now)
    {
        if (Type != ChatType.Group)
        {
            return Result.Fail(ChatErrors.InvalidRole);
        }

        if (title is not null)
        {
            if (!IsValidTitle(title))
            {
                return Result.Fail(ChatErrors.InvalidTitle);
            }

            Title = title.Trim();
        }

        if (description is not null)
        {
            if (description.Length > 512)
            {
                return Result.Fail(ChatErrors.InvalidDescription);
            }

            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }

        if (membersCanInvite.HasValue)
        {
            MembersCanInvite = membersCanInvite.Value;
        }

        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>Admin-only channel profile edit. Null args leave the field unchanged.</summary>
    public Result UpdateChannelProfile(string? title, string? description, DateTime now)
    {
        if (Type != ChatType.Channel)
        {
            return Result.Fail(ChatErrors.InvalidRole);
        }

        if (title is not null)
        {
            if (!IsValidTitle(title))
            {
                return Result.Fail(ChatErrors.InvalidTitle);
            }

            Title = title.Trim();
        }

        if (description is not null)
        {
            if (description.Length > 512)
            {
                return Result.Fail(ChatErrors.InvalidDescription);
            }

            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        }

        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>Admin-only channel visibility switch. Usernames are public-channel-only.</summary>
    public Result SetVisibility(bool isPublic, string? username, DateTime now)
    {
        if (Type != ChatType.Channel)
        {
            return Result.Fail(ChatErrors.InvalidRole);
        }

        if (isPublic)
        {
            if (string.IsNullOrWhiteSpace(username) || !IsValidUsername(username))
            {
                return Result.Fail(ChatErrors.UsernameInvalid);
            }

            IsPublic = true;
            Username = NormalizeUsername(username);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                return Result.Fail(ChatErrors.UsernameInvalid);
            }

            IsPublic = false;
            Username = null;
        }

        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>Admin-only: (re)creates the invite link. Groups and channels only.</summary>
    public Result RotateInviteCode(string code, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (Type == ChatType.Dm)
        {
            return Result.Fail(ChatErrors.DmMembershipFixed);
        }

        InviteCode = code.Trim();
        UpdatedAt = now;
        return Result.Ok();
    }

    public void RevokeInviteCode(DateTime now)
    {
        InviteCode = null;
        UpdatedAt = now;
    }

    public void SetAvatar(string objectKey, string url, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        AvatarObjectKey = objectKey;
        AvatarUrl = url;
        UpdatedAt = now;
    }

    public void RemoveAvatar(DateTime now)
    {
        AvatarObjectKey = null;
        AvatarUrl = null;
        UpdatedAt = now;
    }

    public Result SetMemberMute(string userId, bool muted, DateTime now)
    {
        var member = GetMember(userId);
        if (member is null)
        {
            return Result.Fail(ChatErrors.NotMember);
        }

        _members.Remove(member);
        _members.Add(member with { IsMuted = muted });
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result SetMemberArchived(string userId, bool archived, DateTime now)
    {
        var member = GetMember(userId);
        if (member is null)
        {
            return Result.Fail(ChatErrors.NotMember);
        }

        _members.Remove(member);
        _members.Add(member with { IsArchived = archived });
        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>
    /// Marks the conversation unread from <paramref name="seq"/> onward by moving
    /// the caller's watermark back. The unread count is recomputed from the head.
    /// </summary>
    public Result MarkUnread(string userId, long seq, DateTime now)
    {
        var member = GetMember(userId);
        if (member is null)
        {
            return Result.Fail(ChatErrors.NotMember);
        }

        var target = Math.Clamp(seq - 1, 0, SeqCounter);
        _members.Remove(member);
        _members.Add(member with
        {
            LastReadSeq = Math.Min(member.LastReadSeq, target),
            UnreadCount = (int)Math.Max(0, SeqCounter - Math.Min(member.LastReadSeq, target))
        });
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result Pin(string messageId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (_pinnedMessageIds.Contains(messageId))
        {
            return Result.Fail(ChatErrors.AlreadyPinned);
        }

        if (_pinnedMessageIds.Count >= MaxPins)
        {
            return Result.Fail(ChatErrors.PinLimit);
        }

        _pinnedMessageIds.Add(messageId);
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result Unpin(string messageId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (!_pinnedMessageIds.Remove(messageId))
        {
            return Result.Fail(ChatErrors.NotPinned);
        }

        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>In-memory read of a newly persisted message (single-writer path).</summary>
    public void ApplyNewMessage(long seq, string messageId, string preview, string senderId, DateTime at)
    {
        SeqCounter = Math.Max(SeqCounter, seq);
        LastMessageId = messageId;
        LastMessageAt = at;
        LastMessagePreview = preview;
        UpdatedAt = at;

        for (var i = 0; i < _members.Count; i++)
        {
            var m = _members[i];
            _members[i] = m.UserId == senderId
                ? m with
                {
                    LastReadSeq = Math.Max(m.LastReadSeq, seq),
                    LastReadAt = at,
                    UnreadCount = 0,
                    LastDeliveredSeq = Math.Max(m.LastDeliveredSeq, seq)
                }
                : m with { UnreadCount = m.UnreadCount + 1 };
        }
    }

    public bool MarkRead(string userId, long seq, DateTime at)
    {
        var member = GetMember(userId);
        if (member is null || seq <= member.LastReadSeq)
        {
            return false;
        }

        _members.Remove(member);
        _members.Add(member with
        {
            LastReadSeq = seq,
            LastReadAt = at,
            UnreadCount = (int)Math.Max(0, SeqCounter - seq),
            LastDeliveredSeq = Math.Max(member.LastDeliveredSeq, seq)
        });
        UpdatedAt = at;
        return true;
    }

    /// <summary>
    /// Advances the delivery watermark (recipient confirmed receipt).
    /// Monotonic: stale/duplicate acks are ignored. Reading implies delivery.
    /// </summary>
    public bool MarkDelivered(string userId, long seq, DateTime at)
    {
        var member = GetMember(userId);
        if (member is null || seq <= member.LastDeliveredSeq || seq > SeqCounter)
        {
            return false;
        }

        _members.Remove(member);
        _members.Add(member with { LastDeliveredSeq = seq });
        UpdatedAt = at;
        return true;
    }

    public void SoftDelete(DateTime now)
    {
        IsDeleted = true;
        UpdatedAt = now;
    }

    private bool IsRoleAllowed(ChatRole role) =>
        Type == ChatType.Channel
            ? role is ChatRole.Owner or ChatRole.Admin or ChatRole.Subscriber or ChatRole.Member
            : role is ChatRole.Owner or ChatRole.Admin or ChatRole.Member;

    /// <summary>
    /// Persistence rehydration. Bypasses factories (no re-validation of
    /// already-stored state). Infrastructure use only.
    /// </summary>
    public static Chat Rehydrate(ChatSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var chat = new Chat(
            snapshot.Id, snapshot.Type, snapshot.Title, snapshot.Description,
            snapshot.OwnerId, snapshot.CreatedAt);

        chat._members.AddRange(snapshot.Members);
        chat._pinnedMessageIds.AddRange(snapshot.PinnedMessageIds);
        chat.MemberCount = snapshot.MemberCount;
        chat.LastMessageId = snapshot.LastMessageId;
        chat.LastMessageAt = snapshot.LastMessageAt;
        chat.LastMessagePreview = snapshot.LastMessagePreview;
        chat.SeqCounter = snapshot.SeqCounter;
        chat.IsDeleted = snapshot.IsDeleted;
        chat.CreatedBy = snapshot.CreatedBy;
        chat.UpdatedAt = snapshot.UpdatedAt;
        chat.IsPublic = snapshot.IsPublic;
        chat.Username = snapshot.Username;
        chat.InviteCode = snapshot.InviteCode;
        chat.AvatarObjectKey = snapshot.AvatarObjectKey;
        chat.AvatarUrl = snapshot.AvatarUrl;
        chat.MembersCanInvite = snapshot.MembersCanInvite;
        chat.DmKey = snapshot.DmKey;
        // CreatedAt is set by ctor from snapshot.CreatedAt (matches stored value).
        return chat;
    }

    public static bool IsValidTitle(string title) =>
        !string.IsNullOrWhiteSpace(title) && title.Trim().Length is >= 1 and <= 64;

    public static bool IsValidUsername(string username) =>
        UsernamePattern.IsMatch(username.Trim().ToLowerInvariant());

    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();
}
