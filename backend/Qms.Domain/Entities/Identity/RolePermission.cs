namespace Qms.Domain.Entities;

/// <summary>A permission code (<c>&lt;module&gt;.&lt;action&gt;</c>) granted to a role. The catalog in code is the source of truth (ADR-009 section 3).</summary>
public class RolePermission : TenantEntity
{
    public Guid RoleId { get; set; }

    public required string Permission { get; set; }
}
