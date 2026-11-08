namespace Qms.Domain.Entities;

/// <summary>Base for every table: `id` (uuid v7 generated in the application) and audit timestamps.</summary>
public abstract class Entity : IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency row version: the PostgreSQL system column `xmin` (ADR-008 section 5). Read-only for
    /// application code; the database sets it on every insert and update. A handler that checks a client `version` copies
    /// the value to <c>entry.OriginalValues</c> before saving. There is no real column behind it (no migration DDL).
    /// </summary>
    public uint RowVersion { get; set; }
}
