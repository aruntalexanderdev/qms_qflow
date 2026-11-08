using Microsoft.EntityFrameworkCore;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Support;

/// <summary>Adds rows that reference a service type or priority type, so a test can make exactly one kind of reference.</summary>
public static class SetupReferences
{
    public static async Task AddCompanyMappingAsync(this SetupHost setup, Guid serviceTypeId, Guid priorityTypeId)
    {
        await using var db = setup.Host.OpenContext();
        db.CompanyServicePriorities.Add(new CompanyServicePriority { CompanyId = SeedIds.Company1, ServiceTypeId = serviceTypeId, PriorityTypeId = priorityTypeId });
        await db.SaveChangesAsync();
    }

    public static async Task AddFlowAsync(this SetupHost setup, Guid fromServiceTypeId, Guid toServiceTypeId)
    {
        await using var db = setup.Host.OpenContext();
        db.ServiceFlows.Add(new ServiceFlow
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, FromServiceTypeId = fromServiceTypeId, ToServiceTypeId = toServiceTypeId, Mode = FlowMode.Auto,
        });
        await db.SaveChangesAsync();
    }

    public static async Task AddDispenserMappingAsync(this SetupHost setup, Guid serviceTypeId)
    {
        await using var db = setup.Host.OpenContext();
        db.DispenserZoneServices.Add(new DispenserZoneService
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, DeviceId = SeedIds.Dispenser(1), ZoneId = SeedIds.Zone(1), ServiceTypeId = serviceTypeId, SortOrder = 9,
        });
        await db.SaveChangesAsync();
    }

    public static async Task AddTokenAsync(this SetupHost setup, Guid serviceTypeId, Guid priorityTypeId)
    {
        await using var db = setup.Host.OpenContext();
        var service = await db.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.Id == serviceTypeId);
        var priority = await db.PriorityTypes.IgnoreQueryFilters().SingleAsync(p => p.Id == priorityTypeId);
        db.Tokens.Add(new Token
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, BusinessDate = new DateOnly(2026, 11, 8),
            Number = priority.Prefix + service.Prefix + "0001", PriorityPrefix = priority.Prefix, ServicePrefix = service.Prefix, Sequence = 1,
            PriorityTypeId = priorityTypeId, CurrentServiceTypeId = serviceTypeId, CurrentZoneId = SeedIds.Zone(1),
            IssuedAt = new DateTimeOffset(2026, 11, 8, 6, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();
    }

    public static async Task AddTokenSequenceAsync(this SetupHost setup, Guid serviceTypeId, Guid priorityTypeId)
    {
        await using var db = setup.Host.OpenContext();
        db.TokenSequences.Add(new TokenSequence
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, BusinessDate = new DateOnly(2026, 11, 8),
            ServiceTypeId = serviceTypeId, PriorityTypeId = priorityTypeId, LastSequence = 1,
        });
        await db.SaveChangesAsync();
    }

    public static async Task AddBranchFeeAsync(this SetupHost setup, Guid priorityTypeId)
    {
        await using var db = setup.Host.OpenContext();
        db.BranchPriorityFees.Add(new BranchPriorityFee { CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, PriorityTypeId = priorityTypeId, FeeAmount = 5m });
        await db.SaveChangesAsync();
    }

    public static async Task<bool> ServiceTypeExistsAsync(this SetupHost setup, Guid id)
    {
        await using var db = setup.Host.OpenContext();
        return await db.ServiceTypes.IgnoreQueryFilters().AnyAsync(s => s.Id == id);
    }

    public static async Task<bool> PriorityTypeExistsAsync(this SetupHost setup, Guid id)
    {
        await using var db = setup.Host.OpenContext();
        return await db.PriorityTypes.IgnoreQueryFilters().AnyAsync(p => p.Id == id);
    }
}
