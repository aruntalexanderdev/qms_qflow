namespace Qms.Domain.Entities;

/// <summary>ADR-004: hashed refresh token, rotated on every refresh; reuse revokes the whole family.</summary>
public class RefreshToken : Entity
{
    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash; the token itself is never stored.</summary>
    public required string TokenHash { get; set; }

    public Guid FamilyId { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public Guid? ReplacedById { get; set; }

    public string? CreatedIp { get; set; }
}
