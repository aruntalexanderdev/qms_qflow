using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qms.Application.Auth;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Persistence.Identity;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Support;

/// <summary>The six test users of the M2 setup API tests (ADR-008 section 15).</summary>
public enum SetupUser
{
    /// <summary>Company Admin of Company 1.</summary>
    CompanyAdminC1,

    /// <summary>Company Admin of Company 2.</summary>
    CompanyAdminC2,

    /// <summary>Branch Manager of Branch 1.</summary>
    BranchManagerB1,

    /// <summary>Branch Manager of Branch 2 (a second branch of Company 1).</summary>
    BranchManagerB2,

    /// <summary>Counter Staff at Counter 1 of Branch 1.</summary>
    CounterStaff,

    /// <summary>Super Admin (no company).</summary>
    SuperAdmin,
}

/// <summary>
/// The shared integration-test data of the M2 setup stories (ADR-008 section 15), added to the Company 1 / Branch 1 seed:
/// Company 2 with its own service type (prefix A), priority type (N), branch, zone, counter and dispenser; Branch 2 in
/// Company 1 with a zone, counter and dispenser; and one user per <see cref="SetupUser"/>. Fixed ids, so <see cref="EnsureAsync"/>
/// can run any number of times and changes nothing after the first. Use after <see cref="AuthHost.StartAsync"/>
/// (the host has created the roles by then). Do not combine with <see cref="AuthHost.AddCompany2Async"/> (same company code).
/// </summary>
public sealed class SetupTestData
{
    /// <summary>Password of every user created here (a throw-away test value).</summary>
    public const string Password = "Setup-Test-Passw0rd!";

    public static Guid Company2 { get; } = Make(0x0001);

    /// <summary>Branch 2 of Company 1.</summary>
    public static Guid Branch2 { get; } = Make(0x0002);

    /// <summary>The branch of Company 2.</summary>
    public static Guid Company2Branch { get; } = Make(0x0003);

    public static Guid Branch2Zone { get; } = Make(0x0004);

    public static Guid Branch2Counter { get; } = Make(0x0005);

    public static Guid Branch2Dispenser { get; } = Make(0x0006);

    public static Guid Company2Zone { get; } = Make(0x0007);

    public static Guid Company2Counter { get; } = Make(0x0008);

    public static Guid Company2Dispenser { get; } = Make(0x0009);

    public static Guid Company2ServiceType { get; } = Make(0x000a);

    public static Guid Company2PriorityType { get; } = Make(0x000b);

    private static readonly Dictionary<SetupUser, (string UserName, string Role, Guid? Company)> Definitions = new()
    {
        [SetupUser.CompanyAdminC1] = ("setup.ca.c1", Roles.CompanyAdmin, SeedIds.Company1),
        [SetupUser.CompanyAdminC2] = ("setup.ca.c2", Roles.CompanyAdmin, Company2),
        [SetupUser.BranchManagerB1] = ("setup.bm.b1", Roles.BranchManager, SeedIds.Company1),
        [SetupUser.BranchManagerB2] = ("setup.bm.b2", Roles.BranchManager, SeedIds.Company1),
        [SetupUser.CounterStaff] = ("setup.cs", Roles.CounterStaff, SeedIds.Company1),
        [SetupUser.SuperAdmin] = ("setup.sa", Roles.SuperAdmin, null),
    };

    private readonly AuthHost _host;
    private readonly Dictionary<SetupUser, Guid> _userIds;

    private SetupTestData(AuthHost host, Dictionary<SetupUser, Guid> userIds)
    {
        _host = host;
        _userIds = userIds;
    }

    public Guid UserId(SetupUser user) => _userIds[user];

    public string UserName(SetupUser user) => Definitions[user].UserName;

    /// <summary>A valid bearer token for the user (signed with the test key; the user need not log in).</summary>
    public string Token(SetupUser user)
    {
        var (userName, role, company) = Definitions[user];
        var (branches, counters) = Assignments(user);
        return _host.MintToken(new CurrentUserDto(_userIds[user], userName, userName, role, company, "en", branches, counters));
    }

    /// <summary>Adds what is missing; running it again changes nothing.</summary>
    public static async Task<SetupTestData> EnsureAsync(AuthHost host)
    {
        await using (var db = host.OpenContext())
        {
            await EnsureCompany2Async(db);
            await EnsureBranch2Async(db);
        }

        var userIds = new Dictionary<SetupUser, Guid>();
        using (var scope = host.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            foreach (var (user, (userName, role, company)) in Definitions)
            {
                var existing = await users.FindByNameAsync(userName);
                if (existing is null)
                {
                    existing = new AppUser { UserName = userName, DisplayName = userName, CompanyId = company };
                    Check(await users.CreateAsync(existing, Password));
                    if (role == Roles.SuperAdmin)
                    {
                        Check(await users.AddToRoleAsync(existing, role));
                    }
                }

                userIds[user] = existing.Id;
            }
        }

        await using (var db = host.OpenContext())
        {
            // Company users hold their company's system role through user_role (ADR-009); only Super Admin is an Identity role.
            await EnsureSystemRolesAsync(db, SeedIds.Company1);
            await EnsureSystemRolesAsync(db, Company2);
            foreach (var (user, userId) in userIds)
            {
                var (branches, counters) = Assignments(user);
                var company = Definitions[user].Company;
                if (company is { } roleCompany && Enum.Parse<SystemRoleCode>(Definitions[user].Role) is var code
                    && !await db.CompanyUserRoles.IgnoreQueryFilters().AnyAsync(a => a.UserId == userId))
                {
                    var roleId = await db.CompanyRoles.IgnoreQueryFilters().Where(r => r.CompanyId == roleCompany && r.SystemCode == code).Select(r => r.Id).SingleAsync();
                    db.CompanyUserRoles.Add(new UserRole { CompanyId = roleCompany, UserId = userId, RoleId = roleId });
                }

                foreach (var branchId in branches)
                {
                    if (!await db.UserBranches.IgnoreQueryFilters().AnyAsync(a => a.UserId == userId && a.BranchId == branchId))
                    {
                        db.UserBranches.Add(new UserBranch { CompanyId = company!.Value, UserId = userId, BranchId = branchId });
                    }
                }

                foreach (var counterId in counters)
                {
                    if (!await db.UserCounters.IgnoreQueryFilters().AnyAsync(a => a.UserId == userId && a.CounterId == counterId))
                    {
                        db.UserCounters.Add(new UserCounter { CompanyId = company!.Value, UserId = userId, CounterId = counterId });
                    }
                }
            }

            await db.SaveChangesAsync();
        }

        return new SetupTestData(host, userIds);
    }

    private static (IReadOnlyList<Guid> Branches, IReadOnlyList<Guid> Counters) Assignments(SetupUser user) => user switch
    {
        SetupUser.BranchManagerB1 => ([SeedIds.Branch1], []),
        SetupUser.BranchManagerB2 => ([Branch2], []),
        SetupUser.CounterStaff => ([SeedIds.Branch1], [SeedIds.Counter(1)]),
        _ => ([], []),
    };

    /// <summary>Adds the three system roles with their default grants when the company has none (TASK-015 adds the custom-role helpers).</summary>
    private static async Task EnsureSystemRolesAsync(Qms.Infrastructure.Persistence.QmsDbContext db, Guid companyId)
    {
        var existing = await db.CompanyRoles.IgnoreQueryFilters().Where(r => r.CompanyId == companyId && r.SystemCode != null)
            .Select(r => r.SystemCode!.Value).ToListAsync();
        foreach (var role in DefaultRoles.CreateFor(companyId).Where(r => !existing.Contains(r.SystemCode!.Value)))
        {
            db.CompanyRoles.Add(role);
        }

        await db.SaveChangesAsync();
    }

    private static async Task EnsureCompany2Async(Qms.Infrastructure.Persistence.QmsDbContext db)
    {
        if (!await db.Companies.IgnoreQueryFilters().AnyAsync(c => c.Id == Company2))
        {
            db.Companies.Add(new Company { Id = Company2, Name = "Company 2", Code = "C2", CurrencyCode = "SAR" });
            await db.SaveChangesAsync();
        }

        await EnsureAsync(db, Company2Branch, () => new Branch
        {
            Id = Company2Branch, CompanyId = Company2, Name = "Company 2 branch", Code = "X1", Timezone = "Asia/Riyadh",
        });
        await EnsureAsync(db, Company2Zone, () => new Zone
        {
            Id = Company2Zone, CompanyId = Company2, BranchId = Company2Branch, Name = "Zone X", SortOrder = 1,
        });
        await EnsureAsync(db, Company2Counter, () => new Counter
        {
            Id = Company2Counter, CompanyId = Company2, BranchId = Company2Branch, ZoneId = Company2Zone, Name = "Counter X", Number = 1,
        });
        await EnsureAsync(db, Company2Dispenser, () => new Device
        {
            Id = Company2Dispenser, CompanyId = Company2, BranchId = Company2Branch, ZoneId = Company2Zone, Type = DeviceType.Dispenser, Name = "Dispenser X",
        });
        await EnsureAsync(db, Company2ServiceType, () => new ServiceType
        {
            Id = Company2ServiceType, CompanyId = Company2, Name = "Service X", Prefix = "A", SortOrder = 1,
        });
        await EnsureAsync(db, Company2PriorityType, () => new PriorityType
        {
            Id = Company2PriorityType, CompanyId = Company2, Name = "Normal X", Prefix = "N", PriorityLevel = 2,
        });
    }

    private static async Task EnsureBranch2Async(Qms.Infrastructure.Persistence.QmsDbContext db)
    {
        await EnsureAsync(db, Branch2, () => new Branch
        {
            Id = Branch2, CompanyId = SeedIds.Company1, Name = "Branch 2", Code = "B2", Timezone = "Asia/Dubai",
        });
        await EnsureAsync(db, Branch2Zone, () => new Zone
        {
            Id = Branch2Zone, CompanyId = SeedIds.Company1, BranchId = Branch2, Name = "Zone B2", SortOrder = 1,
        });
        await EnsureAsync(db, Branch2Counter, () => new Counter
        {
            Id = Branch2Counter, CompanyId = SeedIds.Company1, BranchId = Branch2, ZoneId = Branch2Zone, Name = "Counter B2", Number = 1,
        });
        await EnsureAsync(db, Branch2Dispenser, () => new Device
        {
            Id = Branch2Dispenser, CompanyId = SeedIds.Company1, BranchId = Branch2, ZoneId = Branch2Zone, Type = DeviceType.Dispenser, Name = "Dispenser B2",
        });
    }

    private static async Task EnsureAsync<TEntity>(Qms.Infrastructure.Persistence.QmsDbContext db, Guid id, Func<TEntity> create)
        where TEntity : Entity
    {
        if (!await db.Set<TEntity>().IgnoreQueryFilters().AnyAsync(e => e.Id == id))
        {
            db.Set<TEntity>().Add(create());
            await db.SaveChangesAsync();
        }
    }

    private static void Check(IdentityResult result) =>
        result.Succeeded.ShouldBeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));

    // Same layout as SeedIds (version 7, "9" series) so these ids never collide with the seed.
    private static Guid Make(long number) => new($"01930000-0000-7000-8000-{0x0009_0000_0000 + number:x12}");
}
