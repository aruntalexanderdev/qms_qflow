using Microsoft.AspNetCore.Identity;
using Qms.Domain.Entities;

namespace Qms.Infrastructure.Persistence.Identity;

/// <summary>
/// ASP.NET Identity user. Lives in Infrastructure because Identity is a framework dependency (Domain has none).
/// Super Admin has no company. The user tables are not tenant-filtered: sign-in looks users up before the tenant is known.
/// </summary>
public class AppUser : IdentityUser<Guid>, IAuditable
{
    public AppUser()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid? CompanyId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Preferred UI language, `en` or `ar` (the `lang` claim, ADR-004).</summary>
    public string Language { get; set; } = "en";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>ASP.NET Identity role (Super Admin, Company Admin, Branch Manager / Supervisor, Counter Staff).</summary>
public class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
        Id = Guid.CreateVersion7();
    }
}
