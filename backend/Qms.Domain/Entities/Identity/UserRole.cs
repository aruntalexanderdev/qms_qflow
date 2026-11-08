namespace Qms.Domain.Entities;

/// <summary>A company role held by a company user (ADR-009 section 5). A Super Admin (no company) can never hold one.</summary>
public class UserRole : TenantEntity
{
    public Guid UserId { get; set; }

    public Guid RoleId { get; set; }

    public Role? Role { get; set; }
}
