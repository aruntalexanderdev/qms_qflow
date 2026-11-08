namespace Qms.Application.Common;

/// <summary>
/// The company whose data the current request may see (ADR-003). Implemented in Qms.Api from the user's
/// <c>company_id</c> claim or the device's company. <c>null</c> means no company: the tenant query filter then
/// returns nothing (fail closed). Super Admin handlers marked <c>[CrossTenant]</c> use <c>IgnoreQueryFilters()</c>.
/// </summary>
public interface ICurrentTenant
{
    Guid? CompanyId { get; }
}
