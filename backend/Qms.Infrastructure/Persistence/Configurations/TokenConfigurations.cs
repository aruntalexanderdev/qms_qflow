using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qms.Domain.Entities;
using Qms.Infrastructure.Persistence.Identity;

namespace Qms.Infrastructure.Persistence.Configurations;

internal sealed class TokenConfiguration() : EntityConfiguration<Token>("token")
{
    protected override void Map(EntityTypeBuilder<Token> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_token_priority_prefix", "priority_prefix ~ '^[A-Z]$'");
            table.HasCheckConstraint("ck_token_service_prefix", "service_prefix ~ '^[A-Z]$'");
            table.HasCheckConstraint("ck_token_sequence", "sequence BETWEEN 1 AND 9999");
            table.HasCheckConstraint("ck_token_fee_not_negative", "fee_amount IS NULL OR fee_amount >= 0");
        });
        builder.Property(t => t.Number).HasMaxLength(8).IsRequired();
        builder.Property(t => t.PriorityPrefix).HasMaxLength(1).IsFixedLength().IsRequired();
        builder.Property(t => t.ServicePrefix).HasMaxLength(1).IsFixedLength().IsRequired();
        builder.Property(t => t.CustomerCountryCode).HasMaxLength(8);
        builder.Property(t => t.CustomerPhone).HasMaxLength(32);
        builder.Property(t => t.FeeAmount).HasColumnType(ColumnTypes.Money);
        builder.Property(t => t.FeeNote).HasMaxLength(500);
        builder.HasOne(t => t.Branch).WithMany().HasForeignKey(t => new { t.CompanyId, t.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        builder.HasOne(t => t.PriorityType).WithMany().HasForeignKey(t => new { t.CompanyId, t.PriorityTypeId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id });
        builder.HasOne(t => t.CurrentServiceType).WithMany().HasForeignKey(t => new { t.CompanyId, t.CurrentServiceTypeId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id });
        builder.HasOne(t => t.CurrentZone).WithMany().HasForeignKey(t => new { t.BranchId, t.CurrentZoneId })
            .HasPrincipalKey(z => new { z.BranchId, z.Id });
        builder.HasOne(t => t.IssuedDevice).WithMany().HasForeignKey(t => new { t.BranchId, t.IssuedDeviceId })
            .HasPrincipalKey(d => new { d.BranchId, d.Id });
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.IssuedByUserId);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.FeeSettledByUserId);
        // Safety net behind the row-locked token_sequence.
        builder.HasIndex(t => new { t.BranchId, t.BusinessDate, t.Number }).IsUnique().HasDatabaseName("ux_token_branch_date_number");
        builder.HasIndex(t => new { t.BranchId, t.BusinessDate, t.Status });
    }
}

internal sealed class TokenVisitConfiguration() : EntityConfiguration<TokenVisit>("token_visit")
{
    protected override void Map(EntityTypeBuilder<TokenVisit> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_token_visit_recall_count", "recall_count >= 0"));
        builder.Property(v => v.Note).HasMaxLength(500);
        builder.HasOne(v => v.Token).WithMany().HasForeignKey(v => v.TokenId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(v => v.ServiceType).WithMany().HasForeignKey(v => new { v.CompanyId, v.ServiceTypeId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id });
        builder.HasOne(v => v.PriorityType).WithMany().HasForeignKey(v => new { v.CompanyId, v.PriorityTypeId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id });
        builder.HasOne(v => v.Zone).WithMany().HasForeignKey(v => v.ZoneId);
        builder.HasOne(v => v.Counter).WithMany().HasForeignKey(v => v.CounterId);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(v => v.UserId);
        builder.HasIndex(v => v.TokenId);

        // Call Next (spec sections 6 and 9): open visits of one queue, front tokens first, then oldest.
        // Direction is_front DESC matches "ORDER BY is_front DESC, queued_at" so the planner needs no sort.
        builder.HasIndex(v => new { v.ZoneId, v.ServiceTypeId, v.PriorityTypeId, v.IsFront, v.QueuedAt })
            .HasDatabaseName("ix_token_visit_queue")
            .IsDescending(false, false, false, true, false)
            .HasFilter("ended_at IS NULL");
    }
}

internal sealed class TokenSequenceConfiguration() : EntityConfiguration<TokenSequence>("token_sequence")
{
    protected override void Map(EntityTypeBuilder<TokenSequence> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_token_sequence_last_sequence", "last_sequence BETWEEN 0 AND 9999"));
        builder.HasOne(s => s.Branch).WithMany().HasForeignKey(s => new { s.CompanyId, s.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        builder.HasOne(s => s.PriorityType).WithMany().HasForeignKey(s => new { s.CompanyId, s.PriorityTypeId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id });
        builder.HasOne(s => s.ServiceType).WithMany().HasForeignKey(s => new { s.CompanyId, s.ServiceTypeId })
            .HasPrincipalKey(t => new { t.CompanyId, t.Id });
        // One row per branch + business date + priority type + service type; locked while issuing.
        builder.HasIndex(s => new { s.BranchId, s.BusinessDate, s.PriorityTypeId, s.ServiceTypeId }).IsUnique()
            .HasDatabaseName("ux_token_sequence_branch_date_priority_service");
    }
}

internal sealed class AuditLogConfiguration() : EntityConfiguration<AuditLog>("audit_log")
{
    protected override void Map(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.EntityName).HasColumnName("entity").HasMaxLength(100).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Data).HasColumnType("jsonb");
        // No foreign keys to users / devices: audit history must outlive them.
        builder.HasIndex(a => new { a.CompanyId, a.At });
        builder.HasIndex(a => new { a.EntityName, a.EntityId });
    }
}
