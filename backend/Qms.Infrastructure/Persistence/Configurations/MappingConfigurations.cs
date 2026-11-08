using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Qms.Domain.Entities;

namespace Qms.Infrastructure.Persistence.Configurations;

// The mapping chain (spec section 4, common rules section 3) is enforced by composite foreign keys:
//   branch_service_priority  (company_id, service_type_id, priority_type_id) -> company_service_priority
//   counter_service_priority (branch_id,  service_type_id, priority_type_id) -> branch_service_priority
// Each mapping table is also unique on its natural key.

internal sealed class CompanyServicePriorityConfiguration() : EntityConfiguration<CompanyServicePriority>("company_service_priority")
{
    protected override void Map(EntityTypeBuilder<CompanyServicePriority> builder)
    {
        builder.HasOne(m => m.ServiceType).WithMany().HasForeignKey(m => new { m.CompanyId, m.ServiceTypeId })
            .HasPrincipalKey(s => new { s.CompanyId, s.Id });
        builder.HasOne(m => m.PriorityType).WithMany().HasForeignKey(m => new { m.CompanyId, m.PriorityTypeId })
            .HasPrincipalKey(p => new { p.CompanyId, p.Id });
        // Natural key and target of the branch mapping's composite foreign key.
        builder.HasAlternateKey(m => new { m.CompanyId, m.ServiceTypeId, m.PriorityTypeId });
    }
}

internal sealed class BranchServicePriorityConfiguration() : EntityConfiguration<BranchServicePriority>("branch_service_priority")
{
    protected override void Map(EntityTypeBuilder<BranchServicePriority> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint("ck_branch_service_priority_max_tokens", "max_tokens_per_day BETWEEN 1 AND 9999"));
        builder.HasOne(m => m.Branch).WithMany().HasForeignKey(m => new { m.CompanyId, m.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        builder.HasOne(m => m.CompanyServicePriority).WithMany()
            .HasForeignKey(m => new { m.CompanyId, m.ServiceTypeId, m.PriorityTypeId })
            .HasPrincipalKey(c => new { c.CompanyId, c.ServiceTypeId, c.PriorityTypeId });
        // Natural key and target of the counter mapping's composite foreign key.
        builder.HasAlternateKey(m => new { m.BranchId, m.ServiceTypeId, m.PriorityTypeId });
    }
}

internal sealed class CounterServicePriorityConfiguration() : EntityConfiguration<CounterServicePriority>("counter_service_priority")
{
    protected override void Map(EntityTypeBuilder<CounterServicePriority> builder)
    {
        // The branch belongs to the row's company (else the row would be visible to the wrong tenant)...
        builder.HasOne<Branch>().WithMany().HasForeignKey(m => new { m.CompanyId, m.BranchId })
            .HasPrincipalKey(b => new { b.CompanyId, b.Id });
        // ...the counter must be in the same branch as the mapping row...
        builder.HasOne(m => m.Counter).WithMany().HasForeignKey(m => new { m.BranchId, m.CounterId })
            .HasPrincipalKey(c => new { c.BranchId, c.Id });
        // ...and the pair must be in that branch's mapping, Available or not.
        builder.HasOne(m => m.BranchServicePriority).WithMany()
            .HasForeignKey(m => new { m.BranchId, m.ServiceTypeId, m.PriorityTypeId })
            .HasPrincipalKey(b => new { b.BranchId, b.ServiceTypeId, b.PriorityTypeId });
        builder.HasIndex(m => new { m.CounterId, m.ServiceTypeId, m.PriorityTypeId }).IsUnique();
    }
}
