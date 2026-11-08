using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Qms.Application.Common;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Persistence.Identity;

namespace Qms.Infrastructure.Persistence;

/// <summary>
/// The QMS database (spec section 9) plus ASP.NET Identity. Every <see cref="ITenantEntity"/> is filtered on the
/// current company (ADR-003): with no company the filter returns nothing. Use <c>IgnoreQueryFilters()</c> only
/// in handlers marked <c>[CrossTenant]</c>, in the seeder, and in system jobs.
/// </summary>
public class QmsDbContext(DbContextOptions<QmsDbContext> options, ICurrentTenant currentTenant)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(QmsDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Read by the query filters; EF evaluates it per context instance.</summary>
    public Guid? CurrentCompanyId => currentTenant.CompanyId;

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    public DbSet<PriorityType> PriorityTypes => Set<PriorityType>();

    public DbSet<CompanyServicePriority> CompanyServicePriorities => Set<CompanyServicePriority>();

    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<BranchHours> BranchHours => Set<BranchHours>();

    public DbSet<BranchCalendar> BranchCalendars => Set<BranchCalendar>();

    public DbSet<BranchSetting> BranchSettings => Set<BranchSetting>();

    public DbSet<BranchPriorityFee> BranchPriorityFees => Set<BranchPriorityFee>();

    public DbSet<BranchServicePriority> BranchServicePriorities => Set<BranchServicePriority>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<Counter> Counters => Set<Counter>();

    public DbSet<CounterServicePriority> CounterServicePriorities => Set<CounterServicePriority>();

    public DbSet<ServiceFlow> ServiceFlows => Set<ServiceFlow>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<DispenserSetting> DispenserSettings => Set<DispenserSetting>();

    public DbSet<DispenserZoneService> DispenserZoneServices => Set<DispenserZoneService>();

    public DbSet<TicketTemplate> TicketTemplates => Set<TicketTemplate>();

    public DbSet<Token> Tokens => Set<Token>();

    public DbSet<TokenVisit> TokenVisits => Set<TokenVisit>();

    public DbSet<TokenSequence> TokenSequences => Set<TokenSequence>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<MessageLog> MessageLogs => Set<MessageLog>();

    public DbSet<MessagingSetting> MessagingSettings => Set<MessagingSetting>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserBranch> UserBranches => Set<UserBranch>();

    public DbSet<UserCounter> UserCounters => Set<UserCounter>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // Enums are stored as text, never as integers.
        configurationBuilder.Properties<CalendarEntryType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<CounterStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<DayOfWeek>().HaveConversion<string>().HaveMaxLength(16);
        configurationBuilder.Properties<DeviceType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<FeeStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<FlowMode>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<MessageChannel>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<MessageStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<PrinterStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<SkipDefault>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<TokenMode>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<TokenStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<VisitOutcome>().HaveConversion<string>().HaveMaxLength(32);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Identity tables in snake_case like everything else (Identity maps them to AspNet* by default).
        modelBuilder.Entity<AppUser>().ToTable("asp_net_users");
        modelBuilder.Entity<AppRole>().ToTable("asp_net_roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("asp_net_user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("asp_net_user_claims");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("asp_net_role_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("asp_net_user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("asp_net_user_tokens");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(QmsDbContext).Assembly);

        modelBuilder.Entity<Company>().HasQueryFilter(company => company.Id == CurrentCompanyId);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Where(type => typeof(ITenantEntity).IsAssignableFrom(type.ClrType)))
        {
            ApplyTenantFilterMethod.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
        }

        // No cascading deletes by accident: every foreign key we did not configure explicitly restricts.
        foreach (var foreignKey in modelBuilder.Model.GetEntityTypes()
                     .Where(type => type.ClrType.Namespace == typeof(Company).Namespace || type.ClrType == typeof(AppUser))
                     .SelectMany(type => type.GetForeignKeys())
                     .Cast<IConventionForeignKey>()
                     .Where(key => key.GetDeleteBehaviorConfigurationSource() is null or ConfigurationSource.Convention))
        {
            foreignKey.SetDeleteBehavior(DeleteBehavior.Restrict);
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantEntity =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.CompanyId == CurrentCompanyId);

    /// <summary>
    /// A tenant row never changes company after insert (always checked). With a current company: stamp it on new
    /// tenant rows that have none, and refuse to write any row of another company (ADR-003); modified and deleted
    /// rows are judged by their ORIGINAL company, so a row loaded with IgnoreQueryFilters cannot be moved into the
    /// current tenant. Without a current company (seeder, Super Admin, system jobs) nothing is stamped or tenant-checked.
    /// </summary>
    private void EnforceTenantRules()
    {
        RejectCompanyChanges();

        var currentCompanyId = CurrentCompanyId;

        foreach (var entry in ChangeTracker.Entries().Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            switch (entry.Entity)
            {
                case ITenantEntity tenantEntity:
                    if (entry.State == EntityState.Added)
                    {
                        if (tenantEntity.CompanyId == Guid.Empty && currentCompanyId is { } stamp)
                        {
                            tenantEntity.CompanyId = stamp;
                        }
                    }
                    else if (entry.OriginalValues.GetValue<Guid>(nameof(ITenantEntity.CompanyId)) != tenantEntity.CompanyId)
                    {
                        throw new TenantViolationException($"The company of a {entry.Metadata.ClrType.Name} row cannot be changed.");
                    }

                    var rowCompanyId = entry.State == EntityState.Added
                        ? tenantEntity.CompanyId
                        : entry.OriginalValues.GetValue<Guid>(nameof(ITenantEntity.CompanyId));
                    if (currentCompanyId is { } companyId && rowCompanyId != companyId)
                    {
                        throw new TenantViolationException($"A {entry.Metadata.ClrType.Name} row belongs to another company.");
                    }

                    break;
                case Company company when currentCompanyId is { } currentId && company.Id != currentId:
                    throw new TenantViolationException("A Company row belongs to another company.");
            }
        }
    }

    /// <summary>
    /// Runs before change detection: EF's own "key property cannot be modified" error would otherwise hide ours on
    /// entities whose alternate key contains company_id. Compares the snapshot of every tracked tenant row.
    /// </summary>
    private void RejectCompanyChanges()
    {
        var autoDetect = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            foreach (var entry in ChangeTracker.Entries<ITenantEntity>().Where(entry => entry.State is not (EntityState.Added or EntityState.Detached)))
            {
                if (entry.OriginalValues.GetValue<Guid>(nameof(ITenantEntity.CompanyId)) != entry.Entity.CompanyId)
                {
                    throw new TenantViolationException($"The company of a {entry.Metadata.ClrType.Name} row cannot be changed.");
                }
            }
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = autoDetect;
        }
    }
}
