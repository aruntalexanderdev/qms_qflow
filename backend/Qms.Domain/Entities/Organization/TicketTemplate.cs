namespace Qms.Domain.Entities;

/// <summary>Printed ticket layout texts, default-language text plus optional translations (ADR-010).</summary>
public class TicketTemplate : TenantEntity
{
    public required string Header { get; set; }

    public Dictionary<string, string> HeaderTranslations { get; set; } = new();

    public required string Footer { get; set; }

    public Dictionary<string, string> FooterTranslations { get; set; } = new();

    public string? LogoUrl { get; set; }

    public bool ShowWait { get; set; }

    public bool ShowPeopleAhead { get; set; }
}
