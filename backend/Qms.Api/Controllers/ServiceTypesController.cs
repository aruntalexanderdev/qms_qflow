using Microsoft.AspNetCore.Mvc;
using Qms.Api.Auth;
using Qms.Application.Auth;
using Qms.Api.Common;
using Qms.Application.Common;
using Qms.Application.ServiceTypes;

namespace Qms.Api.Controllers;

/// <summary>Body of <c>POST /service-types</c>. No <c>companyId</c>: the company comes from the token (ADR-008 section 3).</summary>
public sealed record CreateServiceTypeRequest(
    string? Name, IReadOnlyDictionary<string, string?>? Translations, string? Prefix, int? TargetMinutes, int SortOrder = 0, bool IsActive = true);

/// <summary>Body of <c>PUT /service-types/{id}</c>: every editable field and the <c>version</c> that was read.</summary>
public sealed record UpdateServiceTypeRequest(
    string? Name, IReadOnlyDictionary<string, string?>? Translations, string? Prefix, int? TargetMinutes, int SortOrder, bool IsActive, string? Version);

/// <summary>Service types of the company (spec section 10, ADR-008). Thin: bind, call the handler, map the result.</summary>
[Route(ApiRoutes.Prefix + "/service-types")]
public sealed class ServiceTypesController : QmsControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.ServiceTypes.View)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive,
        [FromServices] IQueryHandler<ListServiceTypesQuery, IReadOnlyList<ServiceTypeDto>> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListServiceTypesQuery(isActive), cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.ServiceTypes.View)]
    public async Task<IActionResult> Get(
        Guid id,
        [FromServices] IQueryHandler<GetServiceTypeQuery, ServiceTypeDto> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetServiceTypeQuery(id), cancellationToken)).ToActionResult(this);

    [HttpPost]
    [RequirePermission(Permissions.ServiceTypes.Create)]
    public async Task<IActionResult> Create(
        CreateServiceTypeRequest request,
        [FromServices] ICommandHandler<CreateServiceTypeCommand, ServiceTypeDto> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreateServiceTypeCommand(
            request.Name, request.Translations, request.Prefix, request.TargetMinutes, request.SortOrder, request.IsActive);
        return (await handler.HandleAsync(command, cancellationToken))
            .ToCreatedResult(this, dto => $"/{ApiRoutes.Prefix}/service-types/{dto.Id}");
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.ServiceTypes.Edit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdateServiceTypeRequest request,
        [FromServices] ICommandHandler<UpdateServiceTypeCommand, ServiceTypeDto> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdateServiceTypeCommand(
            id, request.Name, request.Translations, request.Prefix, request.TargetMinutes, request.SortOrder, request.IsActive, request.Version);
        return (await handler.HandleAsync(command, cancellationToken)).ToActionResult(this);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.ServiceTypes.Delete)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] ICommandHandler<DeleteServiceTypeCommand, Unit> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new DeleteServiceTypeCommand(id), cancellationToken)).ToNoContentResult(this);
}
