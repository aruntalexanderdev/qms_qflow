using Qms.Application.Common;

namespace Qms.Api.Auth;

/// <summary>
/// The one scoped implementation of <see cref="ICurrentTenant"/> and <see cref="ITenantScope"/> (ADR-003 Amendment 1, A.5).
/// The company is the <c>company_id</c> claim of the signed-in user, so a Super Admin has none; outside an HTTP request
/// (seeder, jobs) there is no user and the company is null. STORY-015 adds the device's company.
/// </summary>
public sealed class RequestTenantContext(ICurrentUser currentUser) : ICurrentTenant, ITenantScope
{
    private readonly CrossTenantSection _section = new();

    public Guid? CompanyId => currentUser.CompanyId;

    public bool IsCrossTenant => _section.IsActive;

    public IDisposable BeginCrossTenant() => _section.Begin();
}
