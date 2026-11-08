using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Qms.Application.Auth;
using Qms.Application.Common;

namespace Qms.Api.Auth;

/// <summary>
/// Protects an endpoint with a permission (<see cref="Permissions"/>), for example
/// <c>[RequirePermission(Permissions.ServiceTypes.Edit)]</c>. Anonymous callers get 401, signed-in users without the
/// permission 403. No policy has to be registered: the attribute carries its own requirement.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute, IAuthorizationRequirementData
{
    public string Permission { get; } = permission;

    public IEnumerable<IAuthorizationRequirement> GetRequirements()
    {
        yield return new DenyAnonymousAuthorizationRequirement();
        yield return new PermissionRequirement(Permission);
    }
}

/// <summary>
/// Protects an endpoint that only a Super Admin may use (companies, later licences and global settings; ADR-009 section 1). It checks
/// the <c>role</c> claim, never a permission: a company role can never grant cross-company rights. Anonymous callers get 401, other
/// signed-in users 403.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireSuperAdminAttribute : AuthorizeAttribute
{
    public RequireSuperAdminAttribute()
    {
        Roles = Qms.Application.Auth.Roles.SuperAdmin;
    }
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(IPermissionResolver resolver, ICurrentUser currentUser, IHttpContextAccessor httpContextAccessor)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await resolver.HasPermissionAsync(currentUser, requirement.Permission, httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
