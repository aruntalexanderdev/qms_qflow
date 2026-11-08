namespace Qms.Domain.Entities;

/// <summary>Printed ticket layout texts, English and Arabic.</summary>
public class TicketTemplate : TenantEntity
{
    public required string HeaderEn { get; set; }

    public required string HeaderAr { get; set; }

    public required string FooterEn { get; set; }

    public required string FooterAr { get; set; }

    public string? LogoUrl { get; set; }

    public bool ShowWait { get; set; }

    public bool ShowPeopleAhead { get; set; }
}
