using Qms.Application.Auth;
using Qms.Application.Common;

namespace Qms.Infrastructure.Authorization;

/// <summary>
/// <see cref="IPermissionResolver"/> backed by the static <see cref="RolePermissionMap"/>. A user without a company never
/// holds a setup permission (Super Admin). The configurable-roles story swaps this class for a database-backed one.
/// </summary>
public sealed class StaticRolePermissionResolver : IPermissionResolver
{
    public Task<bool> HasPermissionAsync(ICurrentUser user, string permission, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var allowed = user is { IsAuthenticated: true, CompanyId: not null } && RolePermissionMap.For(user.Role).Contains(permission);
        return Task.FromResult(allowed);
    }
}
