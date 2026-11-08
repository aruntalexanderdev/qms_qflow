using Microsoft.AspNetCore.Mvc;
using Qms.Api.Auth;
using Qms.Api.Common;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.CompanyRoles;

namespace Qms.Api.Controllers;

/// <summary>Body of <c>POST /roles</c>. No <c>companyId</c> (it comes from the token), no <c>isSystem</c> / <c>systemCode</c>. Only <c>isActive</c> and <c>sortOrder</c> default.</summary>
public sealed record CreateRoleRequest(
    string? Name, IReadOnlyDictionary<string, string?>? Translations, string? BranchScope, IReadOnlyList<string?>? Permissions, int SortOrder = 0, bool IsActive = true);

/// <summary>Body of <c>PUT /roles/{id}</c>: every editable field, the complete permission list and the <c>version</c> that was read.</summary>
public sealed record UpdateRoleRequest(
    string? Name, IReadOnlyDictionary<string, string?>? Translations, string? BranchScope, bool? IsActive, int? SortOrder, IReadOnlyList<string?>? Permissions, string? Version);

/// <summary>Company roles (spec section 10, ADR-009, ADR-008). Thin: bind, call the handler, map the result.</summary>
[Route(ApiRoutes.Prefix + "/roles")]
public sealed class RolesController : QmsControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Roles.View)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive,
        [FromServices] IQueryHandler<ListRolesQuery, IReadOnlyList<RoleDto>> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListRolesQuery(isActive), cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.Roles.View)]
    public async Task<IActionResult> Get(
        Guid id,
        [FromServices] IQueryHandler<GetRoleQuery, RoleDto> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetRoleQuery(id), cancellationToken)).ToActionResult(this);

    [HttpPost]
    [RequirePermission(Permissions.Roles.Create)]
    public async Task<IActionResult> Create(
        CreateRoleRequest request,
        [FromServices] ICommandHandler<CreateRoleCommand, RoleDto> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateRoleCommand(request.Name, request.Translations, request.BranchScope, request.IsActive, request.SortOrder, request.Permissions);
        return (await handler.HandleAsync(command, cancellationToken))
            .ToCreatedResult(this, dto => $"/{ApiRoutes.Prefix}/roles/{dto.Id}");
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Roles.Edit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateRoleRequest request,
        [FromServices] ICommandHandler<UpdateRoleCommand, RoleDto> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateRoleCommand(
            id, request.Name, request.Translations, request.BranchScope, request.IsActive, request.SortOrder, request.Permissions, request.Version);
        return (await handler.HandleAsync(command, cancellationToken)).ToActionResult(this);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.Roles.Delete)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] ICommandHandler<DeleteRoleCommand, Unit> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new DeleteRoleCommand(id), cancellationToken)).ToNoContentResult(this);
}
