namespace Qms.Domain.Entities;

/// <summary>Company service mapping (spec 4.1): a service type with an allowed priority type.</summary>
public class CompanyServicePriority : TenantEntity
{
    public Guid ServiceTypeId { get; set; }

    public ServiceType? ServiceType { get; set; }

    public Guid PriorityTypeId { get; set; }

    public PriorityType? PriorityType { get; set; }
}
