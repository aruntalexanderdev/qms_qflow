namespace Qms.Application.Auth;

/// <summary>
/// The signed-in user as returned by login, refresh and <c>GET /auth/me</c>. <see cref="CompanyId"/> is null for a
/// Super Admin. <see cref="BranchIds"/> and <see cref="CounterIds"/> hold the assignments (Branch Manager, Counter
/// Staff); a Company Admin sees every branch of the company without assignments (spec section 8).
/// </summary>
public sealed record CurrentUserDto(
    Guid Id,
    string UserName,
    string Name,
    string Role,
    Guid? CompanyId,
    string Language,
    IReadOnlyList<Guid> BranchIds,
    IReadOnlyList<Guid> CounterIds);

/// <summary>Result of a successful login or refresh. The refresh token goes into the cookie, never into the body.</summary>
public sealed record SessionTokens(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    CurrentUserDto User);

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

public sealed record IssuedRefreshToken(string Value, DateTimeOffset ExpiresAt);

public enum PasswordCheckOutcome
{
    Succeeded,
    InvalidCredentials,
    LockedOut,
}

public sealed record PasswordCheckResult(PasswordCheckOutcome Outcome, CurrentUserDto? User = null);

public enum RefreshRotationOutcome
{
    Rotated,

    /// <summary>Unknown or expired token.</summary>
    Invalid,

    /// <summary>The token was already rotated or revoked: the whole family has been revoked.</summary>
    Reused,
}

public sealed record RefreshRotation(RefreshRotationOutcome Outcome, Guid UserId = default, IssuedRefreshToken? Token = null);
