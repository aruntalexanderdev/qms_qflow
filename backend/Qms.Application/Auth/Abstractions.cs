namespace Qms.Application.Auth;

/// <summary>User accounts (ASP.NET Core Identity). Implemented in Qms.Infrastructure.</summary>
public interface IUserAccounts
{
    /// <summary>
    /// Checks the password and applies the lockout (5 failures lock the user for 15 minutes). An unknown user, an
    /// inactive user and a wrong password give the same <see cref="PasswordCheckOutcome.InvalidCredentials"/>
    /// with comparable timing.
    /// </summary>
    Task<PasswordCheckResult> CheckPasswordAsync(string userName, string password, CancellationToken cancellationToken);

    /// <summary>An active user with branch and counter assignments, or null.</summary>
    Task<CurrentUserDto?> FindActiveAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Creates the short-lived access token (JWT, ADR-004).</summary>
public interface ITokenService
{
    AccessToken CreateAccessToken(CurrentUserDto user);
}

/// <summary>Refresh tokens: only their SHA-256 hash is stored; rotated on every use; reuse revokes the family (ADR-004).</summary>
public interface IRefreshTokenStore
{
    /// <summary>Starts a new family (sign-in).</summary>
    Task<IssuedRefreshToken> IssueAsync(Guid userId, string? clientIp, CancellationToken cancellationToken);

    /// <summary>Replaces <paramref name="token"/> with a new token of the same family, or reports why it cannot.</summary>
    Task<RefreshRotation> RotateAsync(string token, string? clientIp, CancellationToken cancellationToken);

    /// <summary>Revokes the whole family of <paramref name="token"/>. Unknown tokens are ignored.</summary>
    Task RevokeFamilyAsync(string token, CancellationToken cancellationToken);
}

/// <summary>Looks users up for request-supplied user ids (ADR-003 Amendment 1, B.3).</summary>
public interface IUserDirectory
{
    /// <summary>True when the user exists and belongs to <paramref name="companyId"/>.</summary>
    Task<bool> IsInCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken);
}
