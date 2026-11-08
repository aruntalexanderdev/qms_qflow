using Microsoft.AspNetCore.Mvc;
using Qms.Api.Auth;
using Qms.Api.Common;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.Localisation.CompanyLanguages;

namespace Qms.Api.Controllers;

/// <summary>
/// Body of <c>PUT /company-languages</c>: the complete list of enabled languages and the <c>version</c> that was read.
/// There is no company id (it comes from the token) and no default language (no API changes it, ADR-010 section 1).
/// </summary>
public sealed record UpdateCompanyLanguagesRequest(IReadOnlyList<string?>? EnabledLanguages, string? Version);

/// <summary>The language setting of the caller's company (spec section 10, ADR-010 section 3). Thin: bind, call the handler, map the result.</summary>
[Route(ApiRoutes.Prefix + "/company-languages")]
public sealed class CompanyLanguagesController : QmsControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.CompanyLanguages.View)]
    public async Task<IActionResult> Get(
        [FromServices] IQueryHandler<GetCompanyLanguagesQuery, CompanyLanguagesDto> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new GetCompanyLanguagesQuery(), cancellationToken)).ToActionResult(this);

    [HttpPut]
    [RequirePermission(Permissions.CompanyLanguages.Edit)]
    public async Task<IActionResult> Update(
        UpdateCompanyLanguagesRequest request,
        [FromServices] ICommandHandler<UpdateCompanyLanguagesCommand, CompanyLanguagesDto> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new UpdateCompanyLanguagesCommand(request.EnabledLanguages, request.Version), cancellationToken)).ToActionResult(this);
}
