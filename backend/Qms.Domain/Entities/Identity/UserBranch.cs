namespace Qms.Domain.Entities;

/// <summary>Branch a Branch Manager / Supervisor / Counter Staff user may work in (ADR-003).</summary>
public class UserBranch : TenantEntity
{
    public Guid UserId { get; set; }

    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }
}
