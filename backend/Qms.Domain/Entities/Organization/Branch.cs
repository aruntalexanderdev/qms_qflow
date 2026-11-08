namespace Qms.Domain.Entities;

/// <summary>A physical site. `Timezone` is an IANA id (e.g. Asia/Dubai) used for the business date.</summary>
public class Branch : TenantEntity
{
    public required string NameEn { get; set; }

    public required string NameAr { get; set; }

    public required string Code { get; set; }

    public required string Timezone { get; set; }

    public bool IsActive { get; set; } = true;
}
