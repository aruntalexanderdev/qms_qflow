namespace Qms.Domain.Entities;

/// <summary>
/// Last sequence issued per branch + business date + priority type + service type. Row-locked while issuing:
/// <c>INSERT ... ON CONFLICT DO UPDATE SET last_sequence = last_sequence + 1 RETURNING last_sequence</c>.
/// </summary>
public class TokenSequence : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public DateOnly BusinessDate { get; set; }

    public Guid PriorityTypeId { get; set; }

    public PriorityType? PriorityType { get; set; }

    public Guid ServiceTypeId { get; set; }

    public ServiceType? ServiceType { get; set; }

    public int LastSequence { get; set; }
}
