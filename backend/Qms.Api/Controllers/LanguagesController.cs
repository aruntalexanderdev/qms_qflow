using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Qms.Api.Common;
using Qms.Application.Common;
using Qms.Application.Localisation.Languages;

namespace Qms.Api.Controllers;

/// <summary>
/// The languages on offer to the caller (spec section 10, ADR-010 section 8). Anonymous on purpose: the login page reads
/// it before sign-in. Anonymous callers and Super Admins get the release's available languages, a company user the
/// company's enabled ones. Thin: call the handler, map the result.
/// </summary>
[Route(ApiRoutes.Prefix + "/languages")]
public sealed class LanguagesController : QmsControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> List(
        [FromServices] IQueryHandler<ListLanguagesQuery, LanguagesDto> handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(new ListLanguagesQuery(), cancellationToken)).ToActionResult(this);
}
