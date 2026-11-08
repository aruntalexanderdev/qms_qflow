using Qms.Application.Common;

namespace Qms.Api.Common;

/// <summary>
/// Request-scoped <see cref="ICurrentTenant"/>. Until STORY-003 adds JWT and device authentication there is no
/// signed-in caller, so there is no company: the tenant query filter then returns nothing (fail closed, ADR-003).
/// STORY-003 replaces this with a version that reads the <c>company_id</c> claim.
/// </summary>
public sealed class NoCompanyCurrentTenant : ICurrentTenant
{
    public Guid? CompanyId => null;
}
