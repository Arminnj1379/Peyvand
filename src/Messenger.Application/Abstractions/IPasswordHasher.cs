namespace Messenger.Application.Abstractions;

/// <summary>Password hashing port (PBKDF2 implementation in Infrastructure).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}
