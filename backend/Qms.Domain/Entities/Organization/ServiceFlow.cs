using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>Optional next service after one completes (Auto / Ask / None).</summary>
public class ServiceFlow : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public Guid FromServiceTypeId { get; set; }

    public ServiceType? FromServiceType { get; set; }

    public Guid ToServiceTypeId { get; set; }

    public ServiceType? ToServiceType { get; set; }

    public FlowMode Mode { get; set; } = FlowMode.None;
}
