namespace Qms.Domain.Entities;

/// <summary>Has `created_at` / `updated_at`, set by the auditable interceptor from the injected TimeProvider (UTC).</summary>
public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}
