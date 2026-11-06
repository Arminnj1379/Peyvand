namespace Messenger.Domain.Users;

/// <summary>Primary account type. Exactly one base <see cref="Role"/> always matches it.</summary>
public enum UserType
{
    Employee = 1,
    Customer = 2
}
