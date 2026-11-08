using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>Phase 2: one outbound SMS / WhatsApp message.</summary>
public class MessageLog : TenantEntity
{
    public Guid TokenId { get; set; }

    public Token? Token { get; set; }

    public MessageChannel Channel { get; set; }

    public required string TemplateCode { get; set; }

    /// <summary>Personal data: never log.</summary>
    public required string ToPhone { get; set; }

    public MessageStatus Status { get; set; } = MessageStatus.Queued;

    public string? ProviderRef { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public string? Error { get; set; }
}
