namespace Qms.Domain.Entities;

/// <summary>Who changed what (spec section 9). `Data` is a JSON document.</summary>
public class AuditLog : TenantEntity
{
    public Guid? UserId { get; set; }

    public Guid? DeviceId { get; set; }

    /// <summary>Column `entity`: the kind of record changed, e.g. "token".</summary>
    public required string EntityName { get; set; }

    public Guid? EntityId { get; set; }

    public required string Action { get; set; }

    /// <summary>JSON stored as jsonb. Never put phone numbers in it.</summary>
    public string? Data { get; set; }

    public DateTimeOffset At { get; set; }
}
