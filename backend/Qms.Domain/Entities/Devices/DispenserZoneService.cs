namespace Qms.Domain.Entities;

/// <summary>Dispenser mapping (spec 4.4): a zone + service type a dispenser issues tokens for.</summary>
public class DispenserZoneService : TenantEntity
{
    /// <summary>Carried so the device and the zone are forced to be in the same branch (composite foreign keys).</summary>
    public Guid BranchId { get; set; }

    public Guid DeviceId { get; set; }

    public Device? Device { get; set; }

    public Guid ZoneId { get; set; }

    public Zone? Zone { get; set; }

    public Guid ServiceTypeId { get; set; }

    public ServiceType? ServiceType { get; set; }

    public int SortOrder { get; set; }
}
