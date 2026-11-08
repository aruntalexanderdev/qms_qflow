using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Identity;

namespace Qms.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity behind <see cref="IUserAccounts"/> (sign-in, lockout) and <see cref="IUserDirectory"/>.
/// The user tables are not tenant-filtered: sign-in looks a user up before any company is known.
/// </summary>
public sealed class UserAccounts(UserManager<AppUser> users, IPasswordHasher<AppUser> hasher, QmsDbContext db)
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
    /// TEMPORARY adapter (STORY-048 stage 2, removed by TASK-015): after the ConfigurableRoles migration only SuperAdmin is an
    /// Identity role, so a company user's single <c>role</c> is the system code of their most privileged system role in
    /// <c>user_role</c>. It keeps the static resolver and the scope check working until the database-backed resolver replaces them.
    /// </summary>
    private async Task<string?> SystemRoleCodeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var code = await db.CompanyUserRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(userRole => userRole.UserId == userId && userRole.Role!.SystemCode != null && userRole.Role.IsActive)
            .OrderBy(userRole => userRole.Role!.SortOrder)
            .Select(userRole => userRole.Role!.SystemCode)
            .FirstOrDefaultAsync(cancellationToken);
        return code?.ToString();
    }

    private async Task<CurrentUserDto?> ToDtoAsync(AppUser user, CancellationToken cancellationToken)
    {
        var role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? await SystemRoleCodeAsync(user.Id, cancellationToken);
        if (role is null)
        {
            // A user without a role cannot be authorised for anything.
            return null;
        }

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
            user.Id, user.UserName ?? string.Empty, user.DisplayName, role, user.CompanyId, user.Language, branchIds, counterIds);
    }
}
