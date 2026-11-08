using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence.Identity;

namespace Qms.Infrastructure.Persistence.Configurations;

/// <summary>
/// Constraint names of the role tables (STORY-048). Fixed, because <c>PostgresErrorCatalog</c> and the tests refer to them.
/// <see cref="NameUnique"/> (unique <c>(company_id, lower(name))</c>) and <see cref="UserCompanyForeignKey"/> are raw SQL in the
/// ConfigurableRoles migration: EF cannot model an expression index, nor key the nullable <c>AppUser.CompanyId</c>.
/// </summary>
public static class RoleConstraints
{
    public const string NameUnique = "ux_role_company_id_lower_name";
    public const string SystemCodeUnique = "ux_role_company_id_system_code";
    public const string PermissionUnique = "ux_role_permission_role_id_permission";
    public const string PermissionRoleForeignKey = "fk_role_permission_role_company_id_role_id";
    public const string UserRoleUnique = "ux_user_role_user_id_role_id";
    public const string UserRoleRoleForeignKey = "fk_user_role_role_company_id_role_id";
    public const string UserCompanyForeignKey = "fk_user_role_user_company";
}

internal sealed class RoleConfiguration() : EntityConfiguration<Role>("role")
{
    protected override void Map(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_role_branch_scope", "branch_scope IN ('Company', 'Assigned')");
            table.HasCheckConstraint("ck_role_system_code", "system_code IS NULL OR system_code IN ('CompanyAdmin', 'BranchManager', 'CounterStaff')");
            table.HasCheckConstraint("ck_role_is_system_matches_system_code", "is_system = (system_code IS NOT NULL)");
            table.HasTranslationsCheck("role", "name_translations");
        });
        builder.Property(r => r.Name).HasMaxLength(200).IsRequired();
        builder.Property(r => r.NameTranslations).HasTranslationsMapping();
        builder.Property(r => r.BranchScope).IsRequired();
        builder.Property(r => r.SystemCode);
        builder.Property(r => r.IsSystem).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();
        builder.Property(r => r.SortOrder).IsRequired();
        // Target of the composite foreign keys from role_permission and user_role: same company by construction.
        builder.HasAlternateKey(r => new { r.CompanyId, r.Id });
        builder.HasIndex(r => new { r.CompanyId, r.SystemCode }).IsUnique().HasFilter("system_code IS NOT NULL").HasDatabaseName(RoleConstraints.SystemCodeUnique);
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => new { p.CompanyId, p.RoleId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id }).OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(RoleConstraints.PermissionRoleForeignKey);
    }
}

internal sealed class RolePermissionConfiguration() : EntityConfiguration<RolePermission>("role_permission")
{
    protected override void Map(EntityTypeBuilder<RolePermission> builder)
    {
        // Free text in the database; the catalog in code is the source of truth (ADR-009 section 2).
        builder.Property(p => p.Permission).HasMaxLength(100).IsRequired();
        builder.HasIndex(p => new { p.RoleId, p.Permission }).IsUnique().HasDatabaseName(RoleConstraints.PermissionUnique);
    }
}

internal sealed class UserRoleConfiguration() : EntityConfiguration<UserRole>("user_role")
{
    protected override void Map(EntityTypeBuilder<UserRole> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
        // Same-company guarantee for the role, restrict: a role held by a user cannot be deleted. The user side
        // ((user_id, company_id) -> asp_net_users (id, company_id)) is raw SQL in the ConfigurableRoles migration, like user_branch.
        builder.HasOne(u => u.Role).WithMany().HasForeignKey(u => new { u.CompanyId, u.RoleId })
            .HasPrincipalKey(r => new { r.CompanyId, r.Id }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName(RoleConstraints.UserRoleRoleForeignKey);
        builder.HasIndex(u => new { u.UserId, u.RoleId }).IsUnique().HasDatabaseName(RoleConstraints.UserRoleUnique);
        builder.HasIndex(u => u.UserId);
    }
}
