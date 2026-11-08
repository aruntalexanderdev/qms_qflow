namespace Qms.Domain.Entities;

/// <summary>What the customer comes for. One-letter upper-case prefix, unique per company.</summary>
public class ServiceType : TenantEntity
{
    public required string NameEn { get; set; }

    public required string NameAr { get; set; }

    public required string Prefix { get; set; }

    public int? TargetMinutes { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;
}
