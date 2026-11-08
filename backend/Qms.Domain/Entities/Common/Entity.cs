namespace Qms.Domain.Entities;

/// <summary>Base for every table: `id` (uuid v7 generated in the application) and audit timestamps.</summary>
public abstract class Entity : IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
