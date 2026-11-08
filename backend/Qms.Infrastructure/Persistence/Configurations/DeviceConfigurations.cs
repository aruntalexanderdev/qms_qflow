using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qms.Domain.Entities;

namespace Qms.Infrastructure.Persistence.Configurations;

internal sealed class DeviceConfiguration() : EntityConfiguration<Device>("device")
{
    protected override void Map(EntityTypeBuilder<Device> builder)
    {
        builder.Property(d => d.Name).HasMaxLength(200).IsRequired();
        builder.Property(d => d.DeviceKeyHash).HasMaxLength(128);
        builder.Property(d => d.RegistrationCode).HasMaxLength(64);
        builder.HasOne(d => d.Branch).WithMany().HasForeignKey(d => new { d.CompanyId, d.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        // Installed zone and counter display counter must be in the device's branch (null zone skips the check).
        builder.HasOne(d => d.Zone).WithMany().HasForeignKey(d => new { d.BranchId, d.ZoneId })
            .HasPrincipalKey(z => new { z.BranchId, z.Id });
        builder.HasOne(d => d.Counter).WithMany().HasForeignKey(d => new { d.BranchId, d.CounterId })
            .HasPrincipalKey(c => new { c.BranchId, c.Id });
        builder.HasIndex(d => d.RegistrationCode).IsUnique().HasFilter("registration_code IS NOT NULL");
        builder.HasIndex(d => d.DeviceKeyHash).IsUnique().HasFilter("device_key_hash IS NOT NULL");
        builder.HasAlternateKey(d => new { d.BranchId, d.Id });
    }
}

internal sealed class DispenserSettingConfiguration() : EntityConfiguration<DispenserSetting>("dispenser_setting")
{
    protected override void Map(EntityTypeBuilder<DispenserSetting> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_dispenser_setting_phone_range", "phone_min IS NULL OR phone_max IS NULL OR phone_min <= phone_max");
            table.HasCheckConstraint("ck_dispenser_setting_confirm_seconds", "confirm_seconds >= 0");
        });
        builder.Property(s => s.CountryCodes).HasColumnType("text[]");
        builder.Property(s => s.Languages).HasColumnType("text[]");
        builder.Property(s => s.DefaultCountryCode).HasMaxLength(8);
        builder.Property(s => s.DefaultLanguage).HasMaxLength(8).IsRequired();
        builder.Property(s => s.ShowVip).HasDefaultValue(true);
        builder.HasOne(s => s.Device).WithMany().HasForeignKey(s => s.DeviceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.TicketTemplate).WithMany().HasForeignKey(s => new { s.CompanyId, s.TicketTemplateId })
            .HasPrincipalKey(t => new { t.CompanyId, t.Id });
        builder.HasIndex(s => s.DeviceId).IsUnique();
    }
}

internal sealed class DispenserZoneServiceConfiguration() : EntityConfiguration<DispenserZoneService>("dispenser_zone_service")
{
    protected override void Map(EntityTypeBuilder<DispenserZoneService> builder)
    {
        builder.HasOne(m => m.Device).WithMany().HasForeignKey(m => new { m.BranchId, m.DeviceId })
            .HasPrincipalKey(d => new { d.BranchId, d.Id });
        builder.HasOne(m => m.Zone).WithMany().HasForeignKey(m => new { m.BranchId, m.ZoneId })
            .HasPrincipalKey(z => new { z.BranchId, z.Id });
        builder.HasOne(m => m.ServiceType).WithMany().HasForeignKey(m => new { m.CompanyId, m.ServiceTypeId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id });
        builder.HasIndex(m => new { m.DeviceId, m.ZoneId, m.ServiceTypeId }).IsUnique();
    }
}
