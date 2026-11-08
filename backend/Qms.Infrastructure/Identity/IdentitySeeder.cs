using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Qms.Application.Auth;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Identity;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Infrastructure.Identity;

/// <summary>
/// Development and test seed for the Super Admin Identity role and, when configured, the Super Admin and the Company 1 Company Admin.
/// Only <c>SuperAdmin</c> is an Identity role (ADR-009 section 1); the Company Admin holds the Company 1 system role through <c>user_role</c>.
/// User names and passwords come only from configuration (<c>Seed:SuperAdmin:*</c>, <c>Seed:CompanyAdmin:*</c>: user-secrets
/// or environment variables), never from Git; a user whose section is not configured is skipped. Idempotent: an existing
/// user is left as it is (no password reset). Runs after <see cref="DevSeeder"/>, inside the cross-tenant section.
/// </summary>
public sealed class IdentitySeeder(RoleManager<AppRole> roles, UserManager<AppUser> users, IConfiguration configuration, QmsDbContext db)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!await roles.RoleExistsAsync(Roles.SuperAdmin))
        {
            Ensure(await roles.CreateAsync(new AppRole { Name = Roles.SuperAdmin }), $"role {Roles.SuperAdmin}");
        }

        await SeedUserAsync("Seed:SuperAdmin", Roles.SuperAdmin, null, null, "Super Admin", cancellationToken);
        await SeedUserAsync("Seed:CompanyAdmin", null, SeedIds.Company1, SystemRoleCode.CompanyAdmin, "Company 1 Admin", cancellationToken);
    }

    private async Task SeedUserAsync(
        string section, string? identityRole, Guid? companyId, SystemRoleCode? systemRole, string defaultName, CancellationToken cancellationToken)
    {
        var userName = configuration[$"{section}:UserName"];
        var password = configuration[$"{section}:Password"];
        if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrEmpty(password))
        {
            return;
        }

        if (await users.FindByNameAsync(userName) is not null)
        {
            return;
        }

        var user = new AppUser
        {
            UserName = userName,
            DisplayName = configuration[$"{section}:DisplayName"] is { Length: > 0 } displayName ? displayName : defaultName,
            Language = configuration[$"{section}:Language"] is "ar" ? "ar" : "en",
            CompanyId = companyId,
        };
        Ensure(await users.CreateAsync(user, password), $"user in {section}");
        if (identityRole is not null)
        {
            Ensure(await users.AddToRoleAsync(user, identityRole), $"role of user in {section}");
        }

        if (systemRole is { } code && companyId is { } company)
        {
            // The system roles of the company come from the DevSeeder (or the migration); the user gets one through user_role.
            var roleId = await db.CompanyRoles.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.CompanyId == company && r.SystemCode == code).Select(r => r.Id).SingleAsync(cancellationToken);
            db.CompanyUserRoles.Add(new UserRole { CompanyId = company, UserId = user.Id, RoleId = roleId });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // Never include the password or the identity error descriptions' values; codes are enough.
    private static void Ensure(IdentityResult result, string what)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Seeding {what} failed: {string.Join(", ", result.Errors.Select(error => error.Code))}.");
        }
    }
}
