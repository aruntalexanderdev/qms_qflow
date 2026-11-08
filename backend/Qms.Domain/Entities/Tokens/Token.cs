using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>A customer's token. `Number` is [priority prefix][service prefix][4-digit sequence] and never changes.</summary>
public class Token : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>Business date in the branch timezone.</summary>
    public DateOnly BusinessDate { get; set; }

    public required string Number { get; set; }

    public required string PriorityPrefix { get; set; }

    public required string ServicePrefix { get; set; }

    public int Sequence { get; set; }

    public Guid PriorityTypeId { get; set; }

    public PriorityType? PriorityType { get; set; }

    public Guid CurrentServiceTypeId { get; set; }

    public ServiceType? CurrentServiceType { get; set; }

    public Guid CurrentZoneId { get; set; }

    public Zone? CurrentZone { get; set; }

    public TokenStatus Status { get; set; } = TokenStatus.Waiting;

    public string? CustomerCountryCode { get; set; }

    /// <summary>Personal data: mask in UI, never log.</summary>
    public string? CustomerPhone { get; set; }

    public bool IsPrinted { get; set; }

    public Guid? IssuedDeviceId { get; set; }

    public Device? IssuedDevice { get; set; }

    public Guid? IssuedByUserId { get; set; }

    public DateTimeOffset IssuedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    /// <summary>Fee snapshot at issue, `numeric(12,2)`; null when the priority is not paid.</summary>
    public decimal? FeeAmount { get; set; }

    public FeeStatus FeeStatus { get; set; } = FeeStatus.None;

    public Guid? FeeSettledByUserId { get; set; }

    public DateTimeOffset? FeeSettledAt { get; set; }

    public string? FeeNote { get; set; }
}
