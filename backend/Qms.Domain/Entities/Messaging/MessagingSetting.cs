using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>Phase 2: provider settings per company and channel. Credentials are stored encrypted.</summary>
public class MessagingSetting : TenantEntity
{
    public MessageChannel Channel { get; set; }

    public required string Provider { get; set; }

    /// <summary>Encrypted credentials blob; never plain text.</summary>
    public string? Credentials { get; set; }

    public bool IsEnabled { get; set; }
}
