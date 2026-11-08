using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Qms.Domain.Entities;
using Qms.Domain.Enums;

namespace Qms.Infrastructure.Persistence.Seed;

/// <summary>
/// Seeds Company 1 / Branch 1 exactly as spec section 4 (Development and test environments only; the API
/// decides when to call it). Idempotent: a row is inserted only when its natural key is missing, so a second
/// run changes nothing and edits made by an admin are never overwritten. Users and roles are STORY-003.
/// </summary>
public sealed class DevSeeder(QmsDbContext db)
{
    // Spec 4.1 (company mapping): service number -> priority types allowed.
    private static readonly (int Service, bool Vip)[] CompanyMapping =
    [
        (1, false), (2, false), (2, true), (3, false), (3, true), (4, false), (5, false), (6, false),
    ];

    // Spec 4.2 (branch mapping), all Available, max 100 per day.
    private static readonly (int Service, bool Vip)[] BranchMapping =
    [
        (1, false), (2, false), (2, true), (3, false), (3, true), (4, false), (5, false),
    ];

    // Spec 4.3: counter number -> zone number, then its service + priority pairs.
    private static readonly (int Counter, int Zone, int Service, bool Vip)[] CounterMapping =
    [
        (1, 1, 1, false), (1, 1, 2, false), (1, 1, 3, false), (2, 1, 2, true), (2, 1, 3, true), (3, 2, 4, false), (4, 2, 5, false),
    ];

    // Spec 4.4: dispenser number -> zone number + service number.
    private static readonly (int Dispenser, int Zone, int Service)[] DispenserMapping =
    [
        (1, 1, 1), (1, 1, 2), (2, 1, 3), (2, 2, 4), (2, 2, 5),
    ];

    private static readonly string[] ServicePrefixes = ["A", "B", "C", "D", "F", "G"];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var company = SeedIds.Company1;
        var branch = SeedIds.Branch1;

        await EnsureAsync<Company>(c => c.Id == company, () => new Company
        {
            Id = company, Name = "Company 1", NameTranslations = Arabic("الشركة 1"), DefaultLanguage = "en", EnabledLanguages = ["en"], Code = "C1", CurrencyCode = "AED",
        }, cancellationToken);

        for (var number = 1; number <= ServicePrefixes.Length; number++)
        {
            var n = number;
            await EnsureAsync<ServiceType>(s => s.CompanyId == company && s.Prefix == ServicePrefixes[n - 1], () => new ServiceType
            {
                Id = SeedIds.Service(n), CompanyId = company, Name = $"Service {n}", NameTranslations = Arabic($"الخدمة {n}"),
                Prefix = ServicePrefixes[n - 1], SortOrder = n,
            }, cancellationToken);
        }

        await EnsureAsync<PriorityType>(p => p.CompanyId == company && p.Prefix == "N", () => new PriorityType
        {
            Id = SeedIds.Normal, CompanyId = company, Name = "Normal", NameTranslations = Arabic("عادي"), Prefix = "N", PriorityLevel = 2, IsPaid = false,
        }, cancellationToken);
        await EnsureAsync<PriorityType>(p => p.CompanyId == company && p.Prefix == "V", () => new PriorityType
        {
            Id = SeedIds.Vip, CompanyId = company, Name = "VIP", NameTranslations = Arabic("كبار الشخصيات"), Prefix = "V", PriorityLevel = 1,
            IsPaid = true, FeeAmount = 50.00m,
        }, cancellationToken);

        foreach (var (service, vip) in CompanyMapping)
        {
            var (serviceId, priorityId) = (SeedIds.Service(service), Priority(vip));
            await EnsureAsync<CompanyServicePriority>(
                m => m.CompanyId == company && m.ServiceTypeId == serviceId && m.PriorityTypeId == priorityId,
                () => new CompanyServicePriority
                {
                    Id = SeedIds.Mapping(1, 0, service, vip ? 2 : 1), CompanyId = company, ServiceTypeId = serviceId, PriorityTypeId = priorityId,
                },
                cancellationToken);
        }

        await EnsureAsync<Branch>(b => b.CompanyId == company && b.Code == "B1", () => new Branch
        {
            Id = branch, CompanyId = company, Name = "Branch 1", NameTranslations = Arabic("الفرع 1"), Code = "B1", Timezone = "Asia/Dubai",
        }, cancellationToken);

        // Sample working hours (spec section 4 lists none): Sunday to Thursday, 08:00 to 15:00.
        foreach (var day in new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday })
        {
            var d = day;
            await EnsureAsync<BranchHours>(h => h.BranchId == branch && h.DayOfWeek == d, () => new BranchHours
            {
                CompanyId = company, BranchId = branch, DayOfWeek = d, OpenTime = new TimeOnly(8, 0), CloseTime = new TimeOnly(15, 0),
            }, cancellationToken);
        }

        await EnsureAsync<BranchSetting>(s => s.BranchId == branch, () => new BranchSetting
        {
            CompanyId = company, BranchId = branch, RecallLimit = 3, SkipDefault = SkipDefault.NoShow,
        }, cancellationToken);

        foreach (var (service, vip) in BranchMapping)
        {
            var (serviceId, priorityId) = (SeedIds.Service(service), Priority(vip));
            await EnsureAsync<BranchServicePriority>(
                m => m.BranchId == branch && m.ServiceTypeId == serviceId && m.PriorityTypeId == priorityId,
                () => new BranchServicePriority
                {
                    Id = SeedIds.Mapping(2, 1, service, vip ? 2 : 1), CompanyId = company, BranchId = branch,
                    ServiceTypeId = serviceId, PriorityTypeId = priorityId, MaxTokensPerDay = 100, IsAvailable = true,
                },
                cancellationToken);
        }

        for (var zone = 1; zone <= 2; zone++)
        {
            var z = zone;
            await EnsureAsync<Zone>(x => x.Id == SeedIds.Zone(z), () => new Zone
            {
                Id = SeedIds.Zone(z), CompanyId = company, BranchId = branch, Name = $"Zone {z}", NameTranslations = Arabic($"المنطقة {z}"), SortOrder = z,
            }, cancellationToken);
        }

        foreach (var (counter, zone, _, _) in CounterMapping.DistinctBy(row => row.Counter))
        {
            var (c, z) = (counter, zone);
            await EnsureAsync<Counter>(x => x.BranchId == branch && x.Number == c, () => new Counter
            {
                Id = SeedIds.Counter(c), CompanyId = company, BranchId = branch, ZoneId = SeedIds.Zone(z), Name = $"Counter {c}", Number = c,
                Status = CounterStatus.Closed,
            }, cancellationToken);
        }

        foreach (var (counter, _, service, vip) in CounterMapping)
        {
            var (counterId, serviceId, priorityId) = (SeedIds.Counter(counter), SeedIds.Service(service), Priority(vip));
            await EnsureAsync<CounterServicePriority>(
                m => m.CounterId == counterId && m.ServiceTypeId == serviceId && m.PriorityTypeId == priorityId,
                () => new CounterServicePriority
                {
                    Id = SeedIds.Mapping(3, counter, service, vip ? 2 : 1), CompanyId = company, BranchId = branch, CounterId = counterId,
                    ServiceTypeId = serviceId, PriorityTypeId = priorityId,
                },
                cancellationToken);
        }

        await EnsureAsync<TicketTemplate>(t => t.Id == SeedIds.TicketTemplate1, () => new TicketTemplate
        {
            Id = SeedIds.TicketTemplate1, CompanyId = company, Header = "Company 1", HeaderTranslations = Arabic("الشركة 1"),
            Footer = "Thank you for your visit", FooterTranslations = Arabic("شكراً لزيارتكم"), ShowWait = true, ShowPeopleAhead = true,
        }, cancellationToken);

        for (var dispenser = 1; dispenser <= 2; dispenser++)
        {
            var n = dispenser;
            var deviceId = SeedIds.Dispenser(n);
            await EnsureAsync<Device>(d => d.Id == deviceId, () => new Device
            {
                Id = deviceId, CompanyId = company, BranchId = branch, ZoneId = SeedIds.Zone(1), Type = DeviceType.Dispenser,
                Name = $"Token Dispenser {n}",
            }, cancellationToken);
            await EnsureAsync<DispenserSetting>(s => s.DeviceId == deviceId, () => new DispenserSetting
            {
                CompanyId = company, DeviceId = deviceId, TokenMode = TokenMode.PrintOnly, CountryCodes = ["+971"], DefaultCountryCode = "+971",
                PhoneMin = 9, PhoneMax = 9, Languages = ["en"], DefaultLanguage = "en", TicketTemplateId = SeedIds.TicketTemplate1,
                ConfirmSeconds = 5, ShowVip = true,
            }, cancellationToken);
        }

        foreach (var (dispenser, zone, service) in DispenserMapping)
        {
            var (deviceId, zoneId, serviceId) = (SeedIds.Dispenser(dispenser), SeedIds.Zone(zone), SeedIds.Service(service));
            await EnsureAsync<DispenserZoneService>(
                m => m.DeviceId == deviceId && m.ZoneId == zoneId && m.ServiceTypeId == serviceId,
                () => new DispenserZoneService
                {
                    Id = SeedIds.Mapping(4, dispenser, zone * 10 + service, 0), CompanyId = company, BranchId = branch, DeviceId = deviceId,
                    ZoneId = zoneId, ServiceTypeId = serviceId, SortOrder = service,
                },
                cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The seeded Arabic text stays in the database as <c>translations.ar</c> (ADR-010 section 14). It is not visible while
    /// <c>ar</c> is not available and enabled. The English text is the required default value.
    /// </summary>
    private static Dictionary<string, string> Arabic(string text) => new() { ["ar"] = text };

    private static Guid Priority(bool vip) => vip ? SeedIds.Vip : SeedIds.Normal;

    /// <summary>Adds the row when no row matches its natural key. Filters are ignored: the seeder has no tenant.</summary>
    private async Task EnsureAsync<TEntity>(
        Expression<Func<TEntity, bool>> exists,
        Func<TEntity> create,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (!await db.Set<TEntity>().IgnoreQueryFilters().AnyAsync(exists, cancellationToken))
        {
            db.Set<TEntity>().Add(create());
        }
    }
}
