using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>Registered token dispenser, zone display or counter display.</summary>
public class Device : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    /// <summary>Installed zone. Null for a lobby display that shows all zones.</summary>
    public Guid? ZoneId { get; set; }

    public Zone? Zone { get; set; }

    public DeviceType Type { get; set; }

    public required string Name { get; set; }

    /// <summary>SHA-256 hash of the device key (ADR-004). Never store the key.</summary>
    public string? DeviceKeyHash { get; set; }

    public string? RegistrationCode { get; set; }

    /// <summary>Registration codes are single use and expire after 24 hours (ADR-004).</summary>
    public DateTimeOffset? RegistrationCodeExpiresAt { get; set; }

    /// <summary>Counter shown by a counter display.</summary>
    public Guid? CounterId { get; set; }

    public Counter? Counter { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public PrinterStatus PrinterStatus { get; set; } = PrinterStatus.Unknown;
}
