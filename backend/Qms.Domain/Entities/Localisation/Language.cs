namespace Qms.Domain.Entities;

/// <summary>
/// A language this release ships (ADR-010 section 2). Global registry: not a tenant row, no company filter. `IsAvailable`
/// changes only through a release; companies enable languages from the available ones.
/// </summary>
public class Language : Entity
{
    /// <summary>BCP-47 tag as used in files and APIs (`en`, `ar`). Unique; other tables reference it by foreign key.</summary>
    public required string Code { get; set; }

    /// <summary>Endonym shown in language switches ("English", "العربية"). Never translated.</summary>
    public required string NativeName { get; set; }

    /// <summary>`ltr` or `rtl`.</summary>
    public required string Direction { get; set; }

    public bool IsAvailable { get; set; }

    public int SortOrder { get; set; }
}
