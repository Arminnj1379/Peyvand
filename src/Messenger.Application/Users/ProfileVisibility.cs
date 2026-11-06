using Messenger.Application.Abstractions;
using Messenger.Domain.Users;

namespace Messenger.Application.Users;

/// <summary>
/// Single home of the profile-visibility rule: employees are visible to
/// everyone; customers are visible to employees and to themselves.
/// (Customer-to-customer visibility arrives with the chats module.)
/// </summary>
internal static class ProfileVisibility
{
    public static bool IsEmployee(ICurrentUser caller) =>
        caller.HasRole(nameof(Role.Employee))
        || caller.HasRole(nameof(Role.SystemAdministrator))
        || caller.HasRole(nameof(Role.OrganizationAdministrator));

    public static bool CanView(ICurrentUser caller, User target) =>
        target.Id == caller.UserId || IsEmployee(caller) || target.IsEmployee;

    /// <summary>
    /// Full rule: the base role check, plus members of a shared conversation
    /// (customer-to-customer visibility for people who actually talk).
    /// </summary>
    public static async Task<bool> CanViewAsync(
        ICurrentUser caller, User target, IChatStore chats, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(chats);
        if (CanView(caller, target))
        {
            return true;
        }

        if (caller.UserId is null)
        {
            return false;
        }

        return await chats.SharesChatAsync(caller.UserId, target.Id, ct).ConfigureAwait(false);
    }
}
