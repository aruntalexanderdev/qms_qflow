using Microsoft.AspNetCore.Mvc;
using Qms.Api.Auth;
using Qms.Application.Auth;
using Qms.Api.Common;
using Qms.Application.Common;
using Qms.Application.PriorityTypes;

namespace Qms.Api.Controllers;

/// <summary>Body of <c>POST /priority-types</c>. No <c>companyId</c> and no currency: both come from the company.</summary>
public sealed record CreatePriorityTypeRequest(
    string? NameEn, string? NameAr, string? Prefix, int PriorityLevel, bool IsPaid, decimal? FeeAmount, bool IsActive = true);

/// <summary>Body of <c>PUT /priority-types/{id}</c>: every editable field and the <c>version</c> that was read.</summary>
public sealed record UpdatePriorityTypeRequest(
    string? NameEn, string? NameAr, string? Prefix, int PriorityLevel, bool IsPaid, decimal? FeeAmount, bool IsActive, string? Version);

/// <summary>Priority types of the company (spec sections 4.1 and 10, ADR-008). Thin: bind, call the handler, map the result.</summary>
[Route(ApiRoutes.Prefix + "/priority-types")]
public sealed class PriorityTypesController : QmsControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.PriorityTypes.View)]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive,
        [FromServices] IQueryHandler<ListPriorityTypesQuery, IReadOnlyList<PriorityTypeDto>> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListPriorityTypesQuery(isActive), cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.PriorityTypes.View)]
    public async Task<IActionResult> Get(
        Guid id,
        [FromServices] IQueryHandler<GetPriorityTypeQuery, PriorityTypeDto> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetPriorityTypeQuery(id), cancellationToken)).ToActionResult(this);

    [HttpPost]
    [RequirePermission(Permissions.PriorityTypes.Create)]
    public async Task<IActionResult> Create(
        CreatePriorityTypeRequest request,
        [FromServices] ICommandHandler<CreatePriorityTypeCommand, PriorityTypeDto> handler,
        CancellationToken cancellationToken)
    {
        var command = new CreatePriorityTypeCommand(
            request.NameEn, request.NameAr, request.Prefix, request.PriorityLevel, request.IsPaid, request.FeeAmount, request.IsActive);
        return (await handler.HandleAsync(command, cancellationToken))
            .ToCreatedResult(this, dto => $"/{ApiRoutes.Prefix}/priority-types/{dto.Id}");
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.PriorityTypes.Edit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdatePriorityTypeRequest request,
        [FromServices] ICommandHandler<UpdatePriorityTypeCommand, PriorityTypeDto> handler,
        CancellationToken cancellationToken)
    {
        var command = new UpdatePriorityTypeCommand(
            id, request.NameEn, request.NameAr, request.Prefix, request.PriorityLevel, request.IsPaid, request.FeeAmount, request.IsActive, request.Version);
        return (await handler.HandleAsync(command, cancellationToken)).ToActionResult(this);
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.PriorityTypes.Delete)]
    public async Task<IActionResult> Delete(
        Guid id,
        [FromServices] ICommandHandler<DeletePriorityTypeCommand, Unit> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new DeletePriorityTypeCommand(id), cancellationToken)).ToNoContentResult(this);
}
