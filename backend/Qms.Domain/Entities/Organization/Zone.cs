namespace Qms.Domain.Entities;

/// <summary>Area of a branch where counters sit. A branch with one zone gets a default "Main zone".</summary>
public class Zone : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>Default-language (English) text, required.</summary>
    public required string Name { get; set; }

    /// <summary>Optional translations, language code to text, never the default language (ADR-010 section 4).</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();

    public int SortOrder { get; set; }
}
