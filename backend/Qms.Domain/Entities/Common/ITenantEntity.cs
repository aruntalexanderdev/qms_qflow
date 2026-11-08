namespace Qms.Domain.Entities;

/// <summary>Row owned by one company (ADR-003). The DbContext filters on it and stamps it on insert.</summary>
public interface ITenantEntity
{
    Guid CompanyId { get; set; }
}
