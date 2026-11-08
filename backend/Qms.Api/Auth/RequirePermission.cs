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

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

public sealed class PermissionAuthorizationHandler(IPermissionResolver resolver, ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await resolver.HasPermissionAsync(currentUser, requirement.Permission, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
