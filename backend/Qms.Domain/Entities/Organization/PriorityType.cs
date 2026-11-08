namespace Qms.Domain.Entities;

/// <summary>Normal, VIP ... Level 1 is served first. A paid type needs a fee amount.</summary>
public class PriorityType : TenantEntity
{
    /// <summary>Default-language (English) text, required.</summary>
    public required string Name { get; set; }

    /// <summary>Optional translations, language code to text, never the default language (ADR-010 section 4).</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();

    public required string Prefix { get; set; }

    public int PriorityLevel { get; set; }

    public bool IsPaid { get; set; }

    /// <summary>Company fee, `numeric(12,2)`. Required when <see cref="IsPaid"/>.</summary>
    public decimal? FeeAmount { get; set; }

    public bool IsActive { get; set; } = true;
}
