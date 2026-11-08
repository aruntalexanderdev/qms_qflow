namespace Qms.Domain.Entities;

/// <summary>Normal, VIP ... Level 1 is served first. A paid type needs a fee amount.</summary>
public class PriorityType : TenantEntity
{
    public required string NameEn { get; set; }

    public required string NameAr { get; set; }

    public required string Prefix { get; set; }

    public int PriorityLevel { get; set; }

    public bool IsPaid { get; set; }

    /// <summary>Company fee, `numeric(12,2)`. Required when <see cref="IsPaid"/>.</summary>
    public decimal? FeeAmount { get; set; }

    public bool IsActive { get; set; } = true;
}
