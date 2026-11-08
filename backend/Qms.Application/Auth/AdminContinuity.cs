using Qms.Application.CompanyRoles;
using Qms.Application.Common;
using Qms.Domain.Enums;

namespace Qms.Application.Auth;

/// <summary>
/// One active role of one active user of an active company, with the lock-out permissions (<c>roles.edit</c>, <c>users.edit</c>)
/// that role grants. The store returns one fact per (user, role); <see cref="AdminContinuity"/> combines them per user.
/// </summary>
public sealed record AdministratorFact(Guid UserId, RoleBranchScope BranchScope, IReadOnlyCollection<string> Permissions);

/// <summary>Data access of the lock-out guard. Implemented in Qms.Infrastructure.</summary>
public interface IAdminContinuityStore
{
    /// <summary><c>SELECT ... FOR UPDATE</c> on the company row. Must run inside the transaction of the command.</summary>
    Task LockCompanyAsync(Guid companyId, CancellationToken cancellationToken);

    /// <summary>The facts of every active user of the company (an inactive user, an inactive role or an inactive company gives none).</summary>
    Task<IReadOnlyList<AdministratorFact>> ListAdministratorFactsAsync(Guid companyId, CancellationToken cancellationToken);
}

/// <summary>The pure rules of the lock-out guard (ADR-009 section 7).</summary>
public static class AdminContinuity
{
    /// <summary>The permissions an administrator needs and the caller may not lose.</summary>
    public static IReadOnlyList<string> RequiredPermissions { get; } = [Permissions.Roles.Edit, Permissions.Users.Edit];

    /// <summary>
    /// True when at least one user holds both required permissions (the union over their active roles) and has an active role with
    /// <c>Company</c> scope.
    /// </summary>
    public static bool HasAdministrator(IEnumerable<AdministratorFact> facts) =>
        facts.GroupBy(fact => fact.UserId).Any(user =>
            user.Any(fact => fact.BranchScope == RoleBranchScope.Company)
            && RequiredPermissions.All(required => user.Any(fact => fact.Permissions.Contains(required))));

    /// <summary>True when <paramref name="after"/> lacks a required permission that <paramref name="before"/> had.</summary>
    public static bool LosesRequiredPermission(EffectiveAccess before, EffectiveAccess after) =>
        RequiredPermissions.Any(required => before.Has(required) && !after.Has(required));
}

/// <summary>
/// The shared lock-out guard (ADR-009 section 7), used by the role commands and reusable by the user commands of STORY-011.
/// Pattern inside an audited command: <see cref="BeginAsync"/> first (it locks the company row, so concurrent changes of one
/// company run one after the other), then load and change the rows and save, then <see cref="IAdminContinuityCheck.VerifyAsync"/>.
/// A failed check returns an error; the audit decorator rolls the transaction back, so nothing is kept.
/// </summary>
public interface IAdminContinuityGuard
{
    Task<IAdminContinuityCheck> BeginAsync(CancellationToken cancellationToken);
}

/// <summary>The second half of the guard, after the change has been saved (inside the same transaction).</summary>
public interface IAdminContinuityCheck
{
    /// <summary>
    /// 409 <c>role.cannot_change_self</c> when the change removed <c>roles.edit</c> or <c>users.edit</c> from the caller's own
    /// effective permissions; else 409 <c>role.last_admin</c> when the company is left without an active administrator.
    /// </summary>
    Task<Result> VerifyAsync(CancellationToken cancellationToken);
}

public sealed class AdminContinuityGuard(
    ICurrentUser currentUser, ICurrentTenant tenant, IEffectiveAccessReader accessReader, IAdminContinuityStore store) : IAdminContinuityGuard
{
    public async Task<IAdminContinuityCheck> BeginAsync(CancellationToken cancellationToken)
    {
        var companyId = tenant.CompanyId ?? throw new InvalidOperationException("The lock-out guard ran with no current company.");
        await store.LockCompanyAsync(companyId, cancellationToken);

        // Read after the lock: the state the change starts from, not the one the request was authorised with.
        var before = currentUser.UserId is { } userId ? await accessReader.ReadAsync(userId, cancellationToken) : EffectiveAccess.None;
        return new Check(companyId, currentUser.UserId, before, accessReader, store);
    }

    private sealed class Check(
        Guid companyId, Guid? userId, EffectiveAccess before, IEffectiveAccessReader accessReader, IAdminContinuityStore store) : IAdminContinuityCheck
    {
        public async Task<Result> VerifyAsync(CancellationToken cancellationToken)
        {
            if (userId is { } caller && AdminContinuity.LosesRequiredPermission(before, await accessReader.ReadAsync(caller, cancellationToken)))
            {
                return Result.Failure(RoleErrors.CannotChangeSelf);
            }

            return AdminContinuity.HasAdministrator(await store.ListAdministratorFactsAsync(companyId, cancellationToken))
                ? Result.Success()
                : Result.Failure(RoleErrors.LastAdmin);
        }
    }
}
