namespace Qms.Infrastructure.Persistence.Seed;

/// <summary>
/// Fixed UUIDs (version 7 layout) of the Company 1 / Branch 1 example data (spec section 4), so tests, Postman
/// collections and other seeders can refer to them. Pattern: <c>01930000-0000-7000-8000-&lt;kind&gt;&lt;number&gt;</c>.
/// </summary>
public static class SeedIds
{
    public static Guid Company1 { get; } = Make(0x0001_0000_0001);

    public static Guid Branch1 { get; } = Make(0x0002_0000_0001);

    public static Guid Zone(int number) => Make(0x0003_0000_0000 + number);

    public static Guid Counter(int number) => Make(0x0004_0000_0000 + number);

    public static Guid Service(int number) => Make(0x0005_0000_0000 + number);

    public static Guid Normal { get; } = Make(0x0006_0000_0001);

    public static Guid Vip { get; } = Make(0x0006_0000_0002);

    public static Guid Dispenser(int number) => Make(0x0007_0000_0000 + number);

    public static Guid TicketTemplate1 { get; } = Make(0x0008_0000_0001);

    /// <summary>Id of a mapping row, derived from its two parents so it is stable across runs.</summary>
    internal static Guid Mapping(int kind, int owner, int service, int priority) =>
        Make(((long)(0x0010 + kind) << 32) + (owner * 10_000L) + (service * 10L) + priority);

    private static Guid Make(long suffix) => new($"01930000-0000-7000-8000-{suffix:x12}");
}
