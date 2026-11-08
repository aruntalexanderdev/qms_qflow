using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.Localisation;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Identity;

namespace Qms.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity behind <see cref="IUserAccounts"/> (sign-in, lockout) and <see cref="IUserDirectory"/>.
/// The user tables are not tenant-filtered: sign-in looks a user up before any company is known.
/// </summary>
public sealed class UserAccounts(
    UserManager<AppUser> users, IPasswordHasher<AppUser> hasher, QmsDbContext db, IEffectiveAccessReader accessReader, IRequestLanguage requestLanguage)
    : IUserAccounts, IUserDirectory
{
    // A real hash, built once, so an unknown user costs the same password verification as a known one.
    private static readonly Lazy<(AppUser User, string Hash)> Decoy = new(() =>
    {
        var user = new AppUser { UserName = "decoy" };
        return (user, new PasswordHasher<AppUser>().HashPassword(user, Guid.NewGuid().ToString("N")));
    });

    public async Task<PasswordCheckResult> CheckPasswordAsync(string userName, string password, CancellationToken cancellationToken)
    {
        var user = await users.FindByNameAsync(userName);
        if (user is null || !user.IsActive)
        {
            _ = hasher.VerifyHashedPassword(Decoy.Value.User, Decoy.Value.Hash, password);
            return new PasswordCheckResult(PasswordCheckOutcome.InvalidCredentials);
        }

        if (await users.IsLockedOutAsync(user))
        {
            return new PasswordCheckResult(PasswordCheckOutcome.LockedOut);
        }

        if (!await users.CheckPasswordAsync(user, password))
        {
            // The attempt that reaches the limit still answers invalid_credentials; the next one answers locked_out.
            await users.AccessFailedAsync(user);
            return new PasswordCheckResult(PasswordCheckOutcome.InvalidCredentials);
        }

        await users.ResetAccessFailedCountAsync(user);
        var dto = await ToDtoAsync(user, cancellationToken);
        return dto is null
            ? new PasswordCheckResult(PasswordCheckOutcome.InvalidCredentials)
            : new PasswordCheckResult(PasswordCheckOutcome.Succeeded, dto);
    }

    public async Task<CurrentUserDto?> FindActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is { IsActive: true } ? await ToDtoAsync(user, cancellationToken) : null;
    }

    public Task<bool> IsInCompanyAsync(Guid userId, Guid companyId, CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.CompanyId == companyId, cancellationToken);

    /// <summary>
    /// The user as the auth endpoints return it (ADR-009 section 6): effective access from the user's active roles (one query),
    /// role names resolved for the request's <c>Accept-Language</c> among the company's enabled languages (ADR-010 section 7), and
    /// the branch and counter assignments. A company user with no active role still gets a user, with empty permissions.
    /// </summary>
    private async Task<CurrentUserDto?> ToDtoAsync(AppUser user, CancellationToken cancellationToken)
    {
        var isSuperAdmin = (await users.GetRolesAsync(user)).Contains(Roles.SuperAdmin);
        if (!isSuperAdmin && user.CompanyId is null)
        {
            // Neither a Super Admin nor a company user: cannot be authorised for anything.
            return null;
        }

        var access = isSuperAdmin ? EffectiveAccess.None : await accessReader.ReadAsync(user.Id, cancellationToken);
        var roles = isSuperAdmin ? [] : await RoleNamesAsync(user.CompanyId!.Value, access, cancellationToken);

        // Assignments are tenant rows, and at sign-in there is no company yet: the filter would return nothing.
        // IgnoreQueryFilters is safe because the lookup is by the user's own id (ADR-003 Amendment 1).
        var branchIds = await db.UserBranches.IgnoreQueryFilters().AsNoTracking()
            .Where(assignment => assignment.UserId == user.Id)
            .OrderBy(assignment => assignment.BranchId)
            .Select(assignment => assignment.BranchId)
            .ToListAsync(cancellationToken);
        var counterIds = await db.UserCounters.IgnoreQueryFilters().AsNoTracking()
            .Where(assignment => assignment.UserId == user.Id)
            .OrderBy(assignment => assignment.CounterId)
            .Select(assignment => assignment.CounterId)
            .ToListAsync(cancellationToken);

        return new CurrentUserDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            user.CompanyId,
            user.Language,
            isSuperAdmin,
            roles,
            [.. access.Permissions.Order(StringComparer.Ordinal)],
            isSuperAdmin ? null : access.BranchScope,
            branchIds,
            counterIds);
    }

    private async Task<List<CurrentUserRoleDto>> RoleNamesAsync(Guid companyId, EffectiveAccess access, CancellationToken cancellationToken)
    {
        if (access.Roles.Count == 0)
        {
            return [];
        }

        var company = await db.Companies.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == companyId)
            .Select(row => new { row.DefaultLanguage, row.EnabledLanguages })
            .FirstOrDefaultAsync(cancellationToken);
        var languages = company is null ? CompanyLanguageSettings.English : new CompanyLanguageSettings(company.DefaultLanguage.Trim(), [.. company.EnabledLanguages]);
        var language = requestLanguage.Negotiate(languages);
        return [.. access.Roles.Select(role => new CurrentUserRoleDto(role.Id, LocalizedTextRules.Resolve(role.Name, role.NameTranslations, language, languages)))];
    }
}
