using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>
/// A company role (ADR-009): a named set of permissions plus a branch scope. Super Admin is not a row here (it stays an Identity role).
/// The three system roles carry a <see cref="SystemCode"/>; custom roles have none.
/// </summary>
public class Role : TenantEntity
{
    /// <summary>Default-language (English) text, required, unique per company ignoring case.</summary>
    public required string Name { get; set; }

    /// <summary>Optional translations, language code to text, never the default language (ADR-010 section 4).</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();

    public RoleBranchScope BranchScope { get; set; }

    public bool IsSystem { get; set; }

    /// <summary>Set exactly when <see cref="IsSystem"/> is true.</summary>
    public SystemRoleCode? SystemCode { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public List<RolePermission> Permissions { get; set; } = [];
}
