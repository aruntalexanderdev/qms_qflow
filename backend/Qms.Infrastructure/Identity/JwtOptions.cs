using System.Text;

namespace Qms.Infrastructure.Identity;

/// <summary>
/// Token settings (ADR-004). Section <c>Jwt</c>. <see cref="SigningKey"/> is a secret: user-secrets locally, Key Vault
/// on Azure, never Git. It must be at least 256 bits (32 bytes) of UTF-8.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinimumSigningKeyBytes = 32;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>Tolerance for clock differences when validating <c>exp</c> / <c>nbf</c>.</summary>
    public int ClockSkewSeconds { get; set; } = 30;

    public byte[] SigningKeyBytes => Encoding.UTF8.GetBytes(SigningKey);

    /// <summary>Returns an error message that names the setting, or null when the options are usable.</summary>
    public static string? Validate(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            return "Jwt:Issuer is not configured.";
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            return "Jwt:Audience is not configured.";
        }

        if (string.IsNullOrEmpty(options.SigningKey))
        {
            return "Jwt:SigningKey is not configured. Set it with user-secrets or the environment variable Jwt__SigningKey " +
                   "(at least 32 characters, for example the output of 'openssl rand -base64 48').";
        }

        if (options.SigningKeyBytes.Length < MinimumSigningKeyBytes)
        {
            return $"Jwt:SigningKey must be at least {MinimumSigningKeyBytes} bytes (256 bits).";
        }

        if (options.AccessTokenMinutes is < 1 or > 60)
        {
            return "Jwt:AccessTokenMinutes must be between 1 and 60.";
        }

        return options.RefreshTokenDays is < 1 or > 30 ? "Jwt:RefreshTokenDays must be between 1 and 30." : null;
    }
}
