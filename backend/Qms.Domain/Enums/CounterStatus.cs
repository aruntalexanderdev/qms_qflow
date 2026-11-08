namespace Qms.Domain.Enums;

/// <summary>State of a counter (spec section 9, `counter.status`).</summary>
public enum CounterStatus
{
    Open,
    Paused,
    Closed,
}
