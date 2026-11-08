namespace Qms.Application.Common;

/// <summary>
/// The company whose data the current request may see (ADR-003). Implemented in Qms.Api from the user's
/// <c>company_id</c> claim (later also the device's company). <c>null</c> means no company: the tenant query filter then
/// returns nothing and tenant writes fail with a <c>NoTenant</c> violation, unless a cross-tenant section is open
/// (ADR-003 Amendment 1). Cross-tenant code reads with <c>IgnoreQueryFilters()</c>.
/// </summary>
public interface ICurrentTenant
{
    Guid? CompanyId { get; }

    /// <summary>
    /// True while a cross-tenant section opened with <see cref="ITenantScope.BeginCrossTenant"/> is active.
    /// It allows tenant writes without a current company; it does not switch the read filter off.
    /// </summary>
    bool IsCrossTenant { get; }
}
