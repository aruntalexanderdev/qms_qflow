using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>Holiday or special-hours (e.g. Ramadan) override for a date range, in branch local dates.</summary>
public class BranchCalendar : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public DateOnly DateFrom { get; set; }

    public DateOnly DateTo { get; set; }

    public CalendarEntryType Type { get; set; }

    /// <summary>Required for <see cref="CalendarEntryType.SpecialHours"/>.</summary>
    public TimeOnly? OpenTime { get; set; }

    public TimeOnly? CloseTime { get; set; }
}
