using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>A desk in a zone. The zone must belong to the same branch (composite foreign key).</summary>
public class Counter : TenantEntity
{
    public Guid BranchId { get; set; }

    public Branch? Branch { get; set; }

    public Guid ZoneId { get; set; }

    public Zone? Zone { get; set; }

    public required string Name { get; set; }

    /// <summary>Optional translations of <see cref="Name"/>, language code to text.</summary>
    public Dictionary<string, string> NameTranslations { get; set; } = new();

    public int Number { get; set; }

    public CounterStatus Status { get; set; } = CounterStatus.Closed;

    public Guid? CurrentUserId { get; set; }
}
