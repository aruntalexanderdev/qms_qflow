using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>Per-branch queue settings. At most one row per branch.</summary>
public class BranchSetting : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public int RecallLimit { get; set; } = 3;

    public SkipDefault SkipDefault { get; set; } = SkipDefault.NoShow;

    public bool BlockWhenNoCounter { get; set; }

    public int? WaitAlertMinutes { get; set; }
}
