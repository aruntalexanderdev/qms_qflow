using Microsoft.EntityFrameworkCore;
using Qms.Application.Auth;
using Qms.Application.Common;

namespace Qms.Infrastructure.Scope;

/// <summary>
/// <see cref="IBranchScope"/> (ADR-003 decision 4, ADR-008 section 10). One instance per request: the user's
/// <c>user_branch</c> rows are read at most once, by user id with <c>IgnoreQueryFilters()</c> (the same lookup sign-in
/// does), so the answer does not depend on the tenant filter. A Company Admin needs no assignments.
/// </summary>
public sealed class BranchScope(IQmsDbContext db, ICurrentUser currentUser) : IBranchScope
{
    private HashSet<Guid>? _assigned;

    public async Task<Result> EnsureAccessAsync(Guid branchId, CancellationToken cancellationToken)
    {
        if (!HasCompany)
        {
            return Result.Failure(AuthErrors.Forbidden);
        }

        if (currentUser.IsInRole(Roles.CompanyAdmin))
        {
            // The tenant filter limits this to the company's own branches: other companies and unknown ids are the same 404.
            return await db.Branches.AnyAsync(branch => branch.Id == branchId, cancellationToken)
                ? Result.Success()
                : Result.Failure(CommonErrors.ResourceNotFound);
        }

        if ((await AssignedAsync(cancellationToken)).Contains(branchId))
        {
            return Result.Success();
        }

        return await db.Branches.AnyAsync(branch => branch.Id == branchId, cancellationToken)
            ? Result.Failure(AuthErrors.Forbidden)
            : Result.Failure(CommonErrors.ResourceNotFound);
    }

    public async Task<IReadOnlyCollection<Guid>?> AccessibleBranchIdsAsync(CancellationToken cancellationToken)
    {
        if (!HasCompany)
        {
            return [];
        }

        return currentUser.IsInRole(Roles.CompanyAdmin) ? null : (await AssignedAsync(cancellationToken)).ToArray();
    }

    private bool HasCompany => currentUser is { IsAuthenticated: true, UserId: not null, CompanyId: not null };

    private async Task<HashSet<Guid>> AssignedAsync(CancellationToken cancellationToken)
    {
        if (_assigned is not null)
        {
            return _assigned;
        }

        var userId = currentUser.UserId!.Value;
        var companyId = currentUser.CompanyId!.Value;
        var branchIds = await db.UserBranches.IgnoreQueryFilters().AsNoTracking()
            .Where(assignment => assignment.UserId == userId && assignment.CompanyId == companyId)
            .Select(assignment => assignment.BranchId)
            .ToListAsync(cancellationToken);
        return _assigned = [.. branchIds];
    }
}
