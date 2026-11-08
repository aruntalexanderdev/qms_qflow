using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qms.Domain.Entities;

namespace Qms.Infrastructure.Persistence.Configurations;

internal sealed class LanguageConfiguration() : EntityConfiguration<Language>("language")
{
    protected override void Map(EntityTypeBuilder<Language> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_language_direction", "direction IN ('ltr', 'rtl')"));
        builder.Property(l => l.Code).HasMaxLength(35).IsRequired();
        builder.Property(l => l.NativeName).HasMaxLength(100).IsRequired();
        builder.Property(l => l.Direction).HasMaxLength(3).IsRequired();
        // Unique code, also the target of the language foreign keys (company, user).
        builder.HasAlternateKey(l => l.Code).HasName("ux_language_code");
    }
}

internal sealed class CompanyConfiguration() : EntityConfiguration<Company>("company")
{
    protected override void Map(EntityTypeBuilder<Company> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_company_currency_code", "currency_code ~ '^[A-Z]{3}$'");
            table.HasCheckConstraint("ck_company_default_language_enabled", "default_language = ANY(enabled_languages)");
            table.HasTranslationsCheck("company", "name_translations");
        });
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.NameTranslations).HasTranslationsMapping();
        builder.Property(c => c.DefaultLanguage).HasMaxLength(35).HasDefaultValue("en").IsRequired();
        builder.Property(c => c.EnabledLanguages).HasColumnType("text[]").HasDefaultValueSql("'{en}'::text[]").IsRequired();
        // Language codes are rows of the registry; restrict, so a language in use cannot be removed.
        builder.HasOne<Language>().WithMany().HasForeignKey(c => c.DefaultLanguage).HasPrincipalKey(l => l.Code)
            .OnDelete(DeleteBehavior.Restrict).HasConstraintName("fk_company_default_language_language_code");
        builder.Property(c => c.Code).HasMaxLength(50).IsRequired();
        builder.Property(c => c.LogoUrl).HasMaxLength(500);
        builder.Property(c => c.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
    }
}

internal sealed class ServiceTypeConfiguration() : EntityConfiguration<ServiceType>("service_type")
{
    protected override void Map(EntityTypeBuilder<ServiceType> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_service_type_prefix", "prefix ~ '^[A-Z]$'");
            table.HasCheckConstraint("ck_service_type_target_minutes", "target_minutes IS NULL OR target_minutes > 0");
            table.HasTranslationsCheck("service_type", "name_translations");
        });
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.NameTranslations).HasTranslationsMapping();
        builder.Property(s => s.Prefix).HasMaxLength(1).IsFixedLength().IsRequired();
        builder.HasIndex(s => new { s.CompanyId, s.Prefix }).IsUnique();
        // Target of the composite foreign key from company_service_priority: same company by construction.
        builder.HasAlternateKey(s => new { s.CompanyId, s.Id });
    }
}

internal sealed class PriorityTypeConfiguration() : EntityConfiguration<PriorityType>("priority_type")
{
    protected override void Map(EntityTypeBuilder<PriorityType> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_priority_type_prefix", "prefix ~ '^[A-Z]$'");
            table.HasCheckConstraint("ck_priority_type_priority_level", "priority_level >= 1");
            // A paid priority needs a fee; no fee is ever negative.
            table.HasCheckConstraint("ck_priority_type_fee_required_when_paid", "NOT is_paid OR fee_amount IS NOT NULL");
            table.HasCheckConstraint("ck_priority_type_fee_not_negative", "fee_amount IS NULL OR fee_amount >= 0");
            table.HasTranslationsCheck("priority_type", "name_translations");
        });
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.NameTranslations).HasTranslationsMapping();
        builder.Property(p => p.Prefix).HasMaxLength(1).IsFixedLength().IsRequired();
        builder.Property(p => p.FeeAmount).HasColumnType(ColumnTypes.Money);
        builder.HasIndex(p => new { p.CompanyId, p.Prefix }).IsUnique();
        builder.HasAlternateKey(p => new { p.CompanyId, p.Id });
    }
}

internal sealed class BranchConfiguration() : EntityConfiguration<Branch>("branch")
{
    protected override void Map(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable(table => table.HasTranslationsCheck("branch", "name_translations"));
        builder.Property(b => b.Name).HasMaxLength(200).IsRequired();
        builder.Property(b => b.NameTranslations).HasTranslationsMapping();
        builder.Property(b => b.Code).HasMaxLength(50).IsRequired();
        builder.Property(b => b.Timezone).HasMaxLength(64).IsRequired();
        builder.HasIndex(b => new { b.CompanyId, b.Code }).IsUnique();
        // Target of the composite foreign keys from branch child tables: the child carries the branch's own company.
        builder.HasAlternateKey(b => new { b.CompanyId, b.Id });
    }
}

internal sealed class BranchHoursConfiguration() : EntityConfiguration<BranchHours>("branch_hours")
{
    protected override void Map(EntityTypeBuilder<BranchHours> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_branch_hours_close_after_open", "close_time > open_time"));
        builder.HasOne(h => h.Branch).WithMany().HasForeignKey(h => new { h.CompanyId, h.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        // Several intervals per weekday are allowed (split shifts), but never the same opening time twice.
        builder.HasIndex(h => new { h.BranchId, h.DayOfWeek, h.OpenTime }).IsUnique();
    }
}

internal sealed class BranchCalendarConfiguration() : EntityConfiguration<BranchCalendar>("branch_calendar")
{
    protected override void Map(EntityTypeBuilder<BranchCalendar> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_branch_calendar_date_range", "date_to >= date_from");
            table.HasCheckConstraint(
                "ck_branch_calendar_special_hours_times",
                "type = 'Holiday' OR (open_time IS NOT NULL AND close_time IS NOT NULL AND close_time > open_time)");
        });
        builder.HasOne(c => c.Branch).WithMany().HasForeignKey(c => new { c.CompanyId, c.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        builder.HasIndex(c => new { c.BranchId, c.DateFrom, c.DateTo });
    }
}

internal sealed class BranchSettingConfiguration() : EntityConfiguration<BranchSetting>("branch_setting")
{
    protected override void Map(EntityTypeBuilder<BranchSetting> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_branch_setting_recall_limit", "recall_limit >= 0");
            table.HasCheckConstraint("ck_branch_setting_wait_alert_minutes", "wait_alert_minutes IS NULL OR wait_alert_minutes > 0");
        });
        builder.HasOne(s => s.Branch).WithMany().HasForeignKey(s => new { s.CompanyId, s.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        builder.HasIndex(s => s.BranchId).IsUnique();
    }
}

internal sealed class BranchPriorityFeeConfiguration() : EntityConfiguration<BranchPriorityFee>("branch_priority_fee")
{
    protected override void Map(EntityTypeBuilder<BranchPriorityFee> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_branch_priority_fee_not_negative", "fee_amount >= 0"));
        builder.Property(f => f.FeeAmount).HasColumnType(ColumnTypes.Money);
        builder.HasOne(f => f.Branch).WithMany().HasForeignKey(f => new { f.CompanyId, f.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        builder.HasOne(f => f.PriorityType).WithMany().HasForeignKey(f => new { f.CompanyId, f.PriorityTypeId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id });
        builder.HasIndex(f => new { f.BranchId, f.PriorityTypeId }).IsUnique();
    }
}

internal sealed class ZoneConfiguration() : EntityConfiguration<Zone>("zone")
{
    protected override void Map(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable(table => table.HasTranslationsCheck("zone", "name_translations"));
        builder.Property(z => z.Name).HasMaxLength(200).IsRequired();
        builder.Property(z => z.NameTranslations).HasTranslationsMapping();
        builder.HasOne(z => z.Branch).WithMany().HasForeignKey(z => new { z.CompanyId, z.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        // Target of composite foreign keys that force a counter / device / token zone into the same branch.
        builder.HasAlternateKey(z => new { z.BranchId, z.Id });
    }
}

internal sealed class CounterConfiguration() : EntityConfiguration<Counter>("counter")
{
    protected override void Map(EntityTypeBuilder<Counter> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_counter_number", "number >= 1");
            table.HasTranslationsCheck("counter", "name_translations");
        });
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.NameTranslations).HasTranslationsMapping();
        builder.HasOne(c => c.Branch).WithMany().HasForeignKey(c => new { c.CompanyId, c.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        builder.HasOne(c => c.Zone).WithMany().HasForeignKey(c => new { c.BranchId, c.ZoneId })
            .HasPrincipalKey(z => new { z.BranchId, z.Id });
        builder.HasOne<Qms.Infrastructure.Persistence.Identity.AppUser>().WithMany().HasForeignKey(c => c.CurrentUserId)
            .OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(c => new { c.BranchId, c.Number }).IsUnique();
        builder.HasIndex(c => c.ZoneId);
        builder.HasAlternateKey(c => new { c.BranchId, c.Id });
        // Target of user_counter (company_id, counter_id): a staff assignment stays inside the counter's company.
        builder.HasAlternateKey(c => new { c.CompanyId, c.Id });
    }
}

internal sealed class ServiceFlowConfiguration() : EntityConfiguration<ServiceFlow>("service_flow")
{
    protected override void Map(EntityTypeBuilder<ServiceFlow> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_service_flow_different_services", "from_service_type_id <> to_service_type_id"));
        builder.HasOne(f => f.Branch).WithMany().HasForeignKey(f => new { f.CompanyId, f.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        builder.HasOne(f => f.FromServiceType).WithMany().HasForeignKey(f => new { f.CompanyId, f.FromServiceTypeId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id });
        builder.HasOne(f => f.ToServiceType).WithMany().HasForeignKey(f => new { f.CompanyId, f.ToServiceTypeId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id });
        builder.HasIndex(f => new { f.BranchId, f.FromServiceTypeId, f.ToServiceTypeId }).IsUnique();
    }
}

internal sealed class TicketTemplateConfiguration() : EntityConfiguration<TicketTemplate>("ticket_template")
{
    protected override void Map(EntityTypeBuilder<TicketTemplate> builder)
    {
        builder.ToTable(table =>
        {
            table.HasTranslationsCheck("ticket_template", "header_translations");
            table.HasTranslationsCheck("ticket_template", "footer_translations");
        });
        builder.Property(t => t.Header).HasMaxLength(500).IsRequired();
        builder.Property(t => t.HeaderTranslations).HasTranslationsMapping();
        builder.Property(t => t.Footer).HasMaxLength(500).IsRequired();
        builder.Property(t => t.FooterTranslations).HasTranslationsMapping();
        builder.Property(t => t.LogoUrl).HasMaxLength(500);
        builder.HasAlternateKey(t => new { t.CompanyId, t.Id });
    }
}
