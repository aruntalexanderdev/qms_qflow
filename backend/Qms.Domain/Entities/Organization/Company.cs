namespace Qms.Domain.Entities;

/// <summary>The bank (tenant). Not a tenant row itself: the tenant filter matches `Id`.</summary>
public class Company : Entity
{
    public required string NameEn { get; set; }

    public required string NameAr { get; set; }

    public required string Code { get; set; }

    public string? LogoUrl { get; set; }

    /// <summary>ISO 4217 currency of fees, e.g. AED.</summary>
    public required string CurrencyCode { get; set; }

    public bool IsActive { get; set; } = true;
}
