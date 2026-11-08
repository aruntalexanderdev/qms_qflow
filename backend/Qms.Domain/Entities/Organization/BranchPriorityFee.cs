namespace Qms.Domain.Entities;

/// <summary>Optional per-branch override of a priority type's company fee.</summary>
public class BranchPriorityFee : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public Guid PriorityTypeId { get; set; }

    public PriorityType? PriorityType { get; set; }

    public decimal FeeAmount { get; set; }
}
