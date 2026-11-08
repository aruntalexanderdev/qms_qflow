namespace Qms.Domain.Enums;

/// <summary>How one stage of a visit (`token_visit`) ended.</summary>
public enum VisitOutcome
{
    Completed,
    Transferred,
    NoShow,
    Held,
    Cancelled,
}
