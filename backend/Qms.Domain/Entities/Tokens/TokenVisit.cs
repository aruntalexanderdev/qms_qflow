using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>One stage of a visit. The queue is branch + zone + service type + priority type.</summary>
public class TokenVisit : TenantEntity
{
    public Guid TokenId { get; set; }

    public Token? Token { get; set; }

    public Guid ServiceTypeId { get; set; }

    public ServiceType? ServiceType { get; set; }

    public Guid PriorityTypeId { get; set; }

    public PriorityType? PriorityType { get; set; }

    public Guid ZoneId { get; set; }

    public Zone? Zone { get; set; }

    public Guid? CounterId { get; set; }

    public Counter? Counter { get; set; }

    public Guid? UserId { get; set; }

    public DateTimeOffset QueuedAt { get; set; }

    public DateTimeOffset? CalledAt { get; set; }

    public int RecallCount { get; set; }

    /// <summary>Served before other tokens of the same level (priority transfer, VIP forwarded into a Normal queue).</summary>
    public bool IsFront { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    public VisitOutcome? Outcome { get; set; }

    public string? Note { get; set; }
}
