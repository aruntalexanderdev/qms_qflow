namespace Qms.Domain.Entities;

/// <summary>Base for tenant tables: carries `company_id` (ADR-003).</summary>
public abstract class TenantEntity : Entity, ITenantEntity
{
    public Guid CompanyId { get; set; }
}
