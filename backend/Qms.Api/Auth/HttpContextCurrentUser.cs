using System.Security.Claims;
using Qms.Application.Auth;
using Qms.Application.Common;

namespace Qms.Api.Auth;

/// <summary>
/// <see cref="ICurrentUser"/> from the validated access token (ADR-003, ADR-004). Outside an HTTP request (seeder, jobs)
/// nobody is signed in. Claim names are the raw JWT names: inbound claim mapping is switched off.
/// </summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? UserId => ReadGuid("sub");

    public Guid? CompanyId => ReadGuid("company_id");

    // Only a Super Admin token carries a role claim; company roles are resolved per request from the database (ADR-009 section 6).
    public bool IsSuperAdmin => IsAuthenticated && string.Equals(Principal!.FindFirst("role")?.Value, Roles.SuperAdmin, StringComparison.Ordinal);

    public string? Language => IsAuthenticated ? Principal!.FindFirst("lang")?.Value : null;

    private Guid? ReadGuid(string claim) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirst(claim)?.Value, out var value) ? value : null;
}
