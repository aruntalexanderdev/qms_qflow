namespace Qms.Domain.Enums;

/// <summary>Printer state reported by a dispenser heartbeat (spec sections 7 and 10).</summary>
public enum PrinterStatus
{
    Unknown,
    Ok,
    PaperOut,
    CoverOpen,
    Error,
}
