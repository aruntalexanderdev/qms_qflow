namespace Qms.Domain.Entities;

/// <summary>Area of a branch where counters sit. A branch with one zone gets a default "Main zone".</summary>
public class Zone : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public required string NameEn { get; set; }

    public required string NameAr { get; set; }

    public int SortOrder { get; set; }
}
