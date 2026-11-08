namespace Qms.Domain.Entities;

/// <summary>Regular opening interval of a branch on one weekday (several rows per day allow split shifts).</summary>
public class BranchHours : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly OpenTime { get; set; }

    public TimeOnly CloseTime { get; set; }
}
