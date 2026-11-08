using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Qms.Domain.Entities;

namespace Qms.Application.Common;

/// <summary>
/// The database as the use cases see it (ADR-008 section 1): the Domain entity sets, saving and transactions.
/// Implemented by <c>QmsDbContext</c> in Qms.Infrastructure. Tenant-owned sets are filtered on the current company
/// (ADR-003); reads project to DTOs with <c>AsNoTracking</c>. Identity users are not exposed: they stay behind ports.
/// </summary>
public interface IQmsDbContext
{
    /// <summary>The language registry: global, not filtered by company (ADR-010 section 2).</summary>
    DbSet<Language> Languages { get; }

    DbSet<Company> Companies { get; }

    DbSet<ServiceType> ServiceTypes { get; }

    DbSet<PriorityType> PriorityTypes { get; }

    DbSet<CompanyServicePriority> CompanyServicePriorities { get; }

    DbSet<Branch> Branches { get; }

    DbSet<BranchHours> BranchHours { get; }

    DbSet<BranchCalendar> BranchCalendars { get; }

    DbSet<BranchSetting> BranchSettings { get; }

    DbSet<BranchPriorityFee> BranchPriorityFees { get; }

    DbSet<BranchServicePriority> BranchServicePriorities { get; }

    DbSet<Zone> Zones { get; }

    DbSet<Counter> Counters { get; }

    DbSet<CounterServicePriority> CounterServicePriorities { get; }

    DbSet<ServiceFlow> ServiceFlows { get; }

    DbSet<Device> Devices { get; }

    DbSet<DispenserSetting> DispenserSettings { get; }

    DbSet<DispenserZoneService> DispenserZoneServices { get; }

    DbSet<TicketTemplate> TicketTemplates { get; }

    DbSet<Token> Tokens { get; }

    DbSet<TokenVisit> TokenVisits { get; }

    DbSet<TokenSequence> TokenSequences { get; }

    DbSet<AuditLog> AuditLogs { get; }

    DbSet<MessageLog> MessageLogs { get; }

    DbSet<MessagingSetting> MessagingSettings { get; }

    DbSet<UserBranch> UserBranches { get; }

    DbSet<UserCounter> UserCounters { get; }

    /// <summary>
    /// Tracking entry of <paramref name="entity"/>. A handler that checks a client <c>version</c> sets
    /// <c>Entry(row).Property(r =&gt; r.RowVersion).OriginalValue</c> before saving (ADR-008 section 5).
    /// </summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Starts a database transaction. <see cref="AuditDecorator{TCommand, TResult}"/> uses it for every audited command.</summary>
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken);
}
