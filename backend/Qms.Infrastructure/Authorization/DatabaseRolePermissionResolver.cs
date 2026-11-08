using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Infrastructure.Persistence;

namespace Qms.Infrastructure.Authorization;

/// <summary>
/// Reads the effective access of a user from the database with one query (ADR-009 section 6): the user is active and in an
/// active company, the user's <c>user_role</c> rows lead to active roles, and each role brings its <c>role_permission</c> codes.
/// Codes that are no longer in the catalog are dropped by <see cref="EffectiveAccess.Combine"/>. A Super Admin has no company
/// and gets nothing. The lookup is by the user's own id with <c>IgnoreQueryFilters()</c>, like the sign-in lookups (ADR-003 Amendment 1).
/// </summary>
public sealed class EffectiveAccessReader(QmsDbContext db) : IEffectiveAccessReader
{
    public async Task<EffectiveAccess> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.CompanyUserRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(userRole => userRole.UserId == userId
                && userRole.Role!.IsActive
                && db.Users.Any(user => user.Id == userId && user.IsActive && user.CompanyId == userRole.CompanyId)
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == userRole.CompanyId && company.IsActive))
            .Select(userRole => new
            {
                userRole.Role!.Id,
                userRole.Role.Name,
                userRole.Role.NameTranslations,
                userRole.Role.BranchScope,
                userRole.Role.SortOrder,
                Permissions = userRole.Role.Permissions.Select(permission => permission.Permission).ToList(),
            })
            .ToListAsync(cancellationToken);

        return EffectiveAccess.Combine(rows.Select(row =>
            new GrantedRole(new EffectiveRole(row.Id, row.Name, row.NameTranslations, row.BranchScope, row.SortOrder), row.Permissions)));
    }
}

/// <summary>
/// <see cref="IPermissionResolver"/> for the request: asks <see cref="IEffectiveAccessReader"/> once and keeps the answer, so the
/// permission check, the branch scope and the handler share one query (AC-22). A role or user change is therefore effective on the
/// next request. A caller who is not signed in, has no company (Super Admin) or no user id gets nothing without a query.
/// </summary>
public sealed class DatabaseRolePermissionResolver(IEffectiveAccessReader reader) : IPermissionResolver
{
    private Guid _userId;
    private Task<EffectiveAccess>? _access;

    public async Task<bool> HasPermissionAsync(ICurrentUser user, string permission, CancellationToken cancellationToken) =>
        (await GetEffectiveAccessAsync(user, cancellationToken)).Has(permission);

    public Task<EffectiveAccess> GetEffectiveAccessAsync(ICurrentUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (user is not { IsAuthenticated: true, IsSuperAdmin: false, CompanyId: not null, UserId: { } userId })
        {
            return Task.FromResult(EffectiveAccess.None);
        }

        if (_access is null || _userId != userId)
        {
            _userId = userId;
            _access = reader.ReadAsync(userId, cancellationToken);
        }

        return _access;
    }
}

/// <summary>Data access of the lock-out guard (ADR-009 section 7).</summary>
public sealed class AdminContinuityStore(QmsDbContext db) : IAdminContinuityStore
{
    public async Task LockCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // Serialises every role or user change of one company; released when the command's transaction ends.
        _ = await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM company WHERE id = {companyId} FOR UPDATE", cancellationToken);
    }

    public async Task<IReadOnlyList<AdministratorFact>> ListAdministratorFactsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        string[] required = [.. AdminContinuity.RequiredPermissions];
        var rows = await db.CompanyUserRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(userRole => userRole.CompanyId == companyId
                && userRole.Role!.IsActive
                && db.Users.Any(user => user.Id == userRole.UserId && user.IsActive)
                && db.Companies.IgnoreQueryFilters().Any(company => company.Id == companyId && company.IsActive))
            .Select(userRole => new
            {
                userRole.UserId,
                userRole.Role!.BranchScope,
                Permissions = userRole.Role.Permissions
                    .Where(permission => required.Contains(permission.Permission))
                    .Select(permission => permission.Permission)
                    .ToList(),
            })
            .ToListAsync(cancellationToken);
        return [.. rows.Select(row => new AdministratorFact(row.UserId, row.BranchScope, row.Permissions))];
    }
}
