namespace Qms.Domain.Entities;

/// <summary>Counter a Counter Staff user may operate (ADR-003).</summary>
public class UserCounter : Entity
{
    public Guid UserId { get; set; }

    public Guid CounterId { get; set; }

    public Counter? Counter { get; set; }
}
