using Microsoft.AspNetCore.Mvc;
using Qms.Api.Auth;
using Qms.Api.Common;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.CompanyRoles;

namespace Qms.Api.Controllers;

/// <summary>The permission catalog (spec section 10, ADR-009 section 3): modules and actions in display order, codes only.</summary>
[Route(ApiRoutes.Prefix + "/permissions")]
public sealed class PermissionsController : QmsControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Roles.View)]
    public async Task<IActionResult> List(
        [FromServices] IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionModuleInfo>> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListPermissionsQuery(), cancellationToken)).ToActionResult(this);
}
