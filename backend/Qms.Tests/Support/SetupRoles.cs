using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Support;

/// <summary>
/// Helpers for tests that change roles by SQL/EF, so that every later endpoint story can write its grant and revoke test
/// (ADR-008 Amendment 1 A): create a custom role, add or remove a permission, give a role to a user, switch users, roles and
/// companies on and off. They write straight to the database (no API), as a setup step, with the company the role belongs to.
/// </summary>
public static class SetupRoles
{
    /// <summary>The id of the system role of a company.</summary>
    public static async Task<Guid> SystemRoleIdAsync(this SetupHost setup, SystemRoleCode code, Guid? companyId = null)
    {
        await using var db = setup.Host.OpenContext();
        var company = companyId ?? SeedIds.Company1;
        return await db.CompanyRoles.IgnoreQueryFilters().Where(r => r.CompanyId == company && r.SystemCode == code).Select(r => r.Id).SingleAsync();
    }

    /// <summary>Creates a custom role in a company (default Company 1) and returns its id.</summary>
    public static async Task<Guid> CreateCustomRoleAsync(
        this SetupHost setup, string name, RoleBranchScope scope, IEnumerable<string> permissions, bool isActive = true, int sortOrder = 10, Guid? companyId = null)
    {
        var company = companyId ?? SeedIds.Company1;
        await using var db = setup.Host.OpenContext();
        var role = new Role
        {
            CompanyId = company,
            Name = name,
            BranchScope = scope,
            IsActive = isActive,
            SortOrder = sortOrder,
            Permissions = [.. permissions.Distinct().Select(permission => new RolePermission { CompanyId = company, Permission = permission })],
        };
        db.CompanyRoles.Add(role);
        await db.SaveChangesAsync();
        return role.Id;
    }

    /// <summary>Adds a permission to a role (a no-op when it is there). Not checked against the catalog or the view rule.</summary>
    public static async Task GrantAsync(this SetupHost setup, Guid roleId, params string[] permissions)
    {
        await using var db = setup.Host.OpenContext();
        var role = await db.CompanyRoles.IgnoreQueryFilters().Include(r => r.Permissions).SingleAsync(r => r.Id == roleId);
        foreach (var permission in permissions.Where(code => role.Permissions.All(p => p.Permission != code)))
        {
            db.RolePermissions.Add(new RolePermission { CompanyId = role.CompanyId, RoleId = roleId, Permission = permission });
        }

        await db.SaveChangesAsync();
    }

    /// <summary>Removes permissions from a role.</summary>
    public static async Task RevokeAsync(this SetupHost setup, Guid roleId, params string[] permissions)
    {
        await using var db = setup.Host.OpenContext();
        await db.RolePermissions.IgnoreQueryFilters().Where(p => p.RoleId == roleId && permissions.Contains(p.Permission)).ExecuteDeleteAsync();
    }

    /// <summary>Gives a role to a user (in addition to the roles the user holds).</summary>
    public static async Task AssignRoleAsync(this SetupHost setup, SetupUser user, Guid roleId) => await setup.AssignRoleAsync(setup.Data.UserId(user), roleId);

    public static async Task AssignRoleAsync(this SetupHost setup, Guid userId, Guid roleId)
    {
        await using var db = setup.Host.OpenContext();
        var companyId = await db.CompanyRoles.IgnoreQueryFilters().Where(r => r.Id == roleId).Select(r => r.CompanyId).SingleAsync();
        db.CompanyUserRoles.Add(new UserRole { CompanyId = companyId, UserId = userId, RoleId = roleId });
        await db.SaveChangesAsync();
    }

    /// <summary>Takes a role away from a user.</summary>
    public static async Task UnassignRoleAsync(this SetupHost setup, SetupUser user, Guid roleId)
    {
        var userId = setup.Data.UserId(user);
        await using var db = setup.Host.OpenContext();
        await db.CompanyUserRoles.IgnoreQueryFilters().Where(r => r.UserId == userId && r.RoleId == roleId).ExecuteDeleteAsync();
    }

    public static async Task SetRoleActiveAsync(this SetupHost setup, Guid roleId, bool isActive)
    {
        await using var db = setup.Host.OpenContext();
        await db.CompanyRoles.IgnoreQueryFilters().Where(r => r.Id == roleId).ExecuteUpdateAsync(set => set.SetProperty(r => r.IsActive, isActive));
    }

    public static async Task SetRoleScopeAsync(this SetupHost setup, Guid roleId, RoleBranchScope scope)
    {
        await using var db = setup.Host.OpenContext();
        await db.CompanyRoles.IgnoreQueryFilters().Where(r => r.Id == roleId).ExecuteUpdateAsync(set => set.SetProperty(r => r.BranchScope, scope));
    }

    public static async Task SetUserActiveAsync(this SetupHost setup, SetupUser user, bool isActive)
    {
        var userId = setup.Data.UserId(user);
        await using var db = setup.Host.OpenContext();
        await db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(set => set.SetProperty(u => u.IsActive, isActive));
    }

    public static async Task SetCompanyActiveAsync(this SetupHost setup, Guid companyId, bool isActive)
    {
        await using var db = setup.Host.OpenContext();
        await db.Companies.IgnoreQueryFilters().Where(c => c.Id == companyId).ExecuteUpdateAsync(set => set.SetProperty(c => c.IsActive, isActive));
    }

    /// <summary>Creates an extra company user (no password use needed: tokens are minted) holding the given roles; returns the user id and a bearer token.</summary>
    public static async Task<(Guid UserId, string Token)> CreateUserWithRolesAsync(this SetupHost setup, string userName, params Guid[] roleIds)
    {
        using var scope = setup.Host.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Qms.Infrastructure.Persistence.Identity.AppUser>>();
        var user = new Qms.Infrastructure.Persistence.Identity.AppUser { UserName = userName, DisplayName = userName, CompanyId = SeedIds.Company1 };
        var created = await users.CreateAsync(user, SetupTestData.Password);
        created.Succeeded.ShouldBeTrue(string.Join(", ", created.Errors.Select(e => e.Code)));
        foreach (var roleId in roleIds)
        {
            await setup.AssignRoleAsync(user.Id, roleId);
        }

        var token = setup.Host.MintToken(new Qms.Application.Auth.CurrentUserDto(user.Id, userName, userName, SeedIds.Company1, "en", false, [], [], null, [], []));
        return (user.Id, token);
    }

    /// <summary>The roles the user holds (role ids), by SQL.</summary>
    public static async Task<List<Guid>> RoleIdsOfAsync(this SetupHost setup, Guid userId)
    {
        await using var db = setup.Host.OpenContext();
        return await db.CompanyUserRoles.IgnoreQueryFilters().Where(r => r.UserId == userId).Select(r => r.RoleId).ToListAsync();
    }
}
