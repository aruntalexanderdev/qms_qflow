using Qms.Application.Common;
using Qms.Domain.Enums;

namespace Qms.Application.Auth;

/// <summary>An active company role of a user, as the resolver and the auth contract need it.</summary>
public sealed record EffectiveRole(
    Guid Id, string Name, IReadOnlyDictionary<string, string> NameTranslations, RoleBranchScope BranchScope, int SortOrder);

/// <summary>An active role with the permission codes stored for it (before they are filtered to the catalog).</summary>
public sealed record GrantedRole(EffectiveRole Role, IReadOnlyCollection<string> Permissions);

/// <summary>
/// What a user may do (ADR-009 sections 5 and 6): the union of the permissions of their active roles, only codes still in the
/// catalog, and the widest branch scope (<c>Company</c> when any active role has it, else <c>Assigned</c>). A user who is not
/// active, whose company is not active, who has no active role, or who is a Super Admin gets <see cref="None"/>.
/// </summary>
public sealed record EffectiveAccess(IReadOnlySet<string> Permissions, RoleBranchScope BranchScope, IReadOnlyList<EffectiveRole> Roles)
{
    public static EffectiveAccess None { get; } = new(new HashSet<string>(StringComparer.Ordinal), RoleBranchScope.Assigned, []);

    public bool Has(string permission) => Permissions.Contains(permission);

    /// <summary>Combines the active roles of a user. Roles are ordered by sort order, then name.</summary>
    public static EffectiveAccess Combine(IEnumerable<GrantedRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var list = roles.ToList();
        if (list.Count == 0)
        {
            return None;
        }

        var permissions = new HashSet<string>(
            list.SelectMany(role => role.Permissions).Where(PermissionCatalog.Contains), StringComparer.Ordinal);
        var scope = list.Any(role => role.Role.BranchScope == RoleBranchScope.Company) ? RoleBranchScope.Company : RoleBranchScope.Assigned;
        var ordered = list.Select(role => role.Role).OrderBy(role => role.SortOrder).ThenBy(role => role.Name, StringComparer.Ordinal).ThenBy(role => role.Id).ToList();
        return new EffectiveAccess(permissions, scope, ordered);
    }
}

/// <summary>
/// Answers what the signed-in user may do. The one place that knows how permissions are granted; endpoints and handlers only ask.
/// Backed by the database (STORY-048): one query per request, memoised for the request, so a change to a role or a user is
/// effective on the next request. Implemented in Qms.Infrastructure.
/// </summary>
public interface IPermissionResolver
{
    Task<bool> HasPermissionAsync(ICurrentUser user, string permission, CancellationToken cancellationToken);

    /// <summary>The effective access of <paramref name="user"/> for this request (memoised).</summary>
    Task<EffectiveAccess> GetEffectiveAccessAsync(ICurrentUser user, CancellationToken cancellationToken);
}

/// <summary>
/// Reads the effective access of one user straight from the database, without the per-request memo (sign-in needs the user being
/// signed in, the lock-out guard needs the state after a change). One query. Implemented in Qms.Infrastructure.
/// </summary>
public interface IEffectiveAccessReader
{
    Task<EffectiveAccess> ReadAsync(Guid userId, CancellationToken cancellationToken);
}
