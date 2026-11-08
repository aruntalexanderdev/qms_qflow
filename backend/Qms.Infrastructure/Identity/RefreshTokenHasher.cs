using System.Security.Cryptography;
using System.Text;
using System.Buffers.Text;

namespace Qms.Infrastructure.Identity;

/// <summary>Refresh token generation (256 random bits) and hashing. Only the SHA-256 hash is stored (ADR-004).</summary>
public static class RefreshTokenHasher
{
    public const int TokenBytes = 32;

    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));

    /// <summary>Lowercase hex SHA-256 (64 characters).</summary>
    public static string Hash(string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    /// <summary>Constant-time comparison of two hashes.</summary>
    public static bool HashesEqual(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(left), Encoding.ASCII.GetBytes(right));
}
