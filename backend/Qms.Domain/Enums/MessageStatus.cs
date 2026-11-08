namespace Qms.Domain.Enums;

/// <summary>Delivery state of a `message_log` row (phase 2).</summary>
public enum MessageStatus
{
    Queued,
    Sent,
    Failed,
}
