namespace Qms.Domain.Entities;

/// <summary>
/// Counter service mapping (spec 4.3). The pair must exist in the branch mapping of the counter's branch
/// (composite foreign key on branch_id + service_type_id + priority_type_id), Available or not.
/// </summary>
public class CounterServicePriority : TenantEntity
{
    public Guid BranchId { get; set; }

    public Guid CounterId { get; set; }

    public Counter? Counter { get; set; }

    public Guid ServiceTypeId { get; set; }

    public Guid PriorityTypeId { get; set; }

    public BranchServicePriority? BranchServicePriority { get; set; }
}
