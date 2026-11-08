using Qms.Domain.Enums;

namespace Qms.Domain.Entities;

/// <summary>Dispenser configuration. One row per dispenser device.</summary>
public class DispenserSetting : TenantEntity
{
    public Guid DeviceId { get; set; }

    public Device? Device { get; set; }

    public TokenMode TokenMode { get; set; } = TokenMode.PrintOnly;

    public string[] CountryCodes { get; set; } = [];

    public string? DefaultCountryCode { get; set; }

    public int? PhoneMin { get; set; }

    public int? PhoneMax { get; set; }

    /// <summary>Language codes offered on the dispenser: a subset of the company's enabled languages (phase 1: en).</summary>
    public string[] Languages { get; set; } = ["en"];

    public string DefaultLanguage { get; set; } = "en";

    public Guid? TicketTemplateId { get; set; }

    public TicketTemplate? TicketTemplate { get; set; }

    public int ConfirmSeconds { get; set; } = 5;

    /// <summary>Show the VIP button on this dispenser. Default true.</summary>
    public bool ShowVip { get; set; } = true;
}
