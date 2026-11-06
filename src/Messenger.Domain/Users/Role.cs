namespace Messenger.Domain.Users;

/// <summary>
/// Authorization roles. Names are stable identifiers: the JWT <c>role</c>
/// claims and API policies use <c>Role.ToString()</c> values directly.
/// </summary>
public enum Role
{
    Employee = 1,
    Customer = 2,
    SupportAgent = 3,
    OrganizationAdministrator = 4,
    SystemAdministrator = 5
}
