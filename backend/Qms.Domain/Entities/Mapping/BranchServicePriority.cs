namespace Qms.Domain.Entities;

/// <summary>
/// Branch service mapping (spec 4.2). The pair must exist in the company mapping (composite foreign key on
/// company_id + service_type_id + priority_type_id).
/// </summary>
public class BranchServicePriority : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public Guid ServiceTypeId { get; set; }

    public Guid PriorityTypeId { get; set; }

    public CompanyServicePriority? CompanyServicePriority { get; set; }

    /// <summary>Daily token limit, 1 to 9999.</summary>
    public int MaxTokensPerDay { get; set; }

    /// <summary>False = Not Available: hidden on dispensers, waiting tokens are still served.</summary>
    public bool IsAvailable { get; set; } = true;
}
