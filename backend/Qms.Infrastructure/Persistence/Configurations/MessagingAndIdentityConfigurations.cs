using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence.Identity;

namespace Qms.Infrastructure.Persistence.Configurations;

internal sealed class MessageLogConfiguration() : EntityConfiguration<MessageLog>("message_log")
{
    protected override void Map(EntityTypeBuilder<MessageLog> builder)
    {
        builder.Property(m => m.TemplateCode).HasMaxLength(100).IsRequired();
        builder.Property(m => m.ToPhone).HasMaxLength(32).IsRequired();
        builder.Property(m => m.ProviderRef).HasMaxLength(200);
        builder.Property(m => m.Error).HasMaxLength(1000);
        builder.HasOne(m => m.Token).WithMany().HasForeignKey(m => new { m.CompanyId, m.TokenId })
            .HasPrincipalKey(t => new { t.CompanyId, t.Id });
        builder.HasIndex(m => m.TokenId);
    }
}

internal sealed class MessagingSettingConfiguration() : EntityConfiguration<MessagingSetting>("messaging_setting")
{
    protected override void Map(EntityTypeBuilder<MessagingSetting> builder)
    {
        builder.Property(m => m.Provider).HasMaxLength(100).IsRequired();
        builder.Property(m => m.Credentials).HasColumnName("credentials");
        builder.HasIndex(m => new { m.CompanyId, m.Channel }).IsUnique();
    }
}

internal sealed class RefreshTokenConfiguration() : EntityConfiguration<RefreshToken>("refresh_token")
{
    protected override void Map(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(r => r.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(r => r.CreatedIp).HasMaxLength(64);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => r.TokenHash).IsUnique();
        builder.HasIndex(r => r.FamilyId);
        builder.HasIndex(r => r.UserId);
    }
}

internal sealed class UserBranchConfiguration() : EntityConfiguration<UserBranch>("user_branch")
{
    protected override void Map(EntityTypeBuilder<UserBranch> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(u => u.Branch).WithMany().HasForeignKey(u => u.BranchId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(u => new { u.UserId, u.BranchId }).IsUnique();
        builder.HasIndex(u => u.BranchId);
    }
}

internal sealed class UserCounterConfiguration() : EntityConfiguration<UserCounter>("user_counter")
{
    protected override void Map(EntityTypeBuilder<UserCounter> builder)
    {
        builder.HasOne<AppUser>().WithMany().HasForeignKey(u => u.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(u => u.Counter).WithMany().HasForeignKey(u => u.CounterId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(u => new { u.UserId, u.CounterId }).IsUnique();
        builder.HasIndex(u => u.CounterId);
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_asp_net_users_language", "language IN ('en', 'ar')"));
        builder.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.Language).HasMaxLength(8).IsRequired();
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.UpdatedAt).IsRequired();
        // Null for Super Admin. Sign-in finds users before a tenant is known, so there is no tenant filter here.
        builder.HasOne<Company>().WithMany().HasForeignKey(u => u.CompanyId);
        builder.HasIndex(u => u.CompanyId);
    }
}
