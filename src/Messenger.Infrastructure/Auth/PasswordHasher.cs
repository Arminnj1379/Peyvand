using System.Security.Cryptography;
using Messenger.Application.Abstractions;

namespace Messenger.Infrastructure.Auth;

/// <summary>
/// PBKDF2-SHA256 password hasher (210k iterations). No external dependency;
/// parameters are versioned in the payload (<c>$pbkdf2-sha256$t=..$m=..$salt$hash</c>)
/// so a future Argon2id upgrade can coexist.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"$pbkdf2-sha256$t={Iterations}$m={Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        ArgumentException.ThrowIfNullOrEmpty(passwordHash);

        try
        {
            var parts = passwordHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4 || parts[0] != "pbkdf2-sha256")
            {
                return false;
            }

            if (!int.TryParse(parts[1].Split('=')[1], out var iterations))
            {
                return false;
            }

            // parts[2] is "m={base64-salt}"; parts[3] is the base64 hash.
            var saltSegment = parts[2];
            if (!saltSegment.StartsWith("m=", StringComparison.Ordinal))
            {
                return false;
            }

            var salt = Convert.FromBase64String(saltSegment[2..]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            // Corrupt stored hash — fail closed, never throw.
            return false;
        }
    }
}
