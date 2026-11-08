namespace Qms.Domain.Enums;

/// <summary>Token lifecycle state (spec section 5.4).</summary>
public enum TokenStatus
{
    Waiting,
    Called,
    Serving,
    Completed,
    Held,
    NoShow,
    Cancelled,
}
