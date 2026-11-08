using System.Security.Claims;
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

    public string? Role => IsAuthenticated ? Principal!.FindFirst("role")?.Value : null;

    public string? Language => IsAuthenticated ? Principal!.FindFirst("lang")?.Value : null;

    public bool IsInRole(string role) => Role is { } current && string.Equals(current, role, StringComparison.Ordinal);

    private Guid? ReadGuid(string claim) =>
        IsAuthenticated && Guid.TryParse(Principal!.FindFirst(claim)?.Value, out var value) ? value : null;
}
