using Qms.Domain.Enums;

namespace Qms.Application.Auth;

/// <summary>A company role of the signed-in user: <c>Name</c> is resolved for the request's <c>Accept-Language</c> (ADR-010 section 7).</summary>
public sealed record CurrentUserRoleDto(Guid Id, string Name);

/// <summary>
/// The signed-in user as returned by login, refresh and <c>GET /auth/me</c> (ADR-009 section 6). <see cref="CompanyId"/> is null
/// for a Super Admin. <see cref="Roles"/> are the user's active company roles, <see cref="Permissions"/> the effective permissions
/// (sorted ordinally), <see cref="BranchScope"/> the widest scope of the active roles (<c>Assigned</c> with no active role, null
/// for a Super Admin). <see cref="BranchIds"/> and <see cref="CounterIds"/> hold the assignments; a user whose scope is
/// <c>Company</c> sees every branch of the company without assignments. There is no single <c>role</c> field any more.
/// </summary>
public sealed record CurrentUserDto(
    Guid Id,
    string UserName,
    string Name,
    Guid? CompanyId,
    string Language,
    bool IsSuperAdmin,
    IReadOnlyList<CurrentUserRoleDto> Roles,
    IReadOnlyList<string> Permissions,
    RoleBranchScope? BranchScope,
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
