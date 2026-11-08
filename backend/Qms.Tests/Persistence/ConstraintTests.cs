using Microsoft.EntityFrameworkCore;
using Qms.Domain.Entities;
using Qms.Domain.Enums;
using Qms.Infrastructure.Persistence;
using Qms.Infrastructure.Persistence.Seed;

namespace Qms.Tests.Persistence;

// STORY-002 AC-5 (uniqueness) and AC-6 (fee constraint), plus the other check constraints of the story.
[Collection(PostgresCollection.Name)]
public sealed class ConstraintTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = new(2026, 11, 8);

    private async Task<QmsDbContext> SeededAsync() =>
        PostgresFixture.CreateContext(await postgres.NewDatabaseAsync(seeded: true));

    private static TokenSequence Sequence(Guid priority, Guid service, int last = 1) => new()
    {
        CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, BusinessDate = Today, PriorityTypeId = priority, ServiceTypeId = service, LastSequence = last,
    };

    private static Token NewToken(string number = "VB0001") => new()
    {
        CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, BusinessDate = Today, Number = number, PriorityPrefix = "V", ServicePrefix = "B", Sequence = 1,
        PriorityTypeId = SeedIds.Vip, CurrentServiceTypeId = SeedIds.Service(2), CurrentZoneId = SeedIds.Zone(1), IssuedAt = DateTimeOffset.UtcNow,
    };

    // ---- AC-5 uniqueness

    [Fact]
    public async Task A_second_token_sequence_row_for_the_same_branch_date_priority_and_service_is_rejected()
    {
        await using var db = await SeededAsync();
        db.TokenSequences.Add(Sequence(SeedIds.Vip, SeedIds.Service(2)));
        await db.SaveChangesAsync();

        db.TokenSequences.Add(Sequence(SeedIds.Vip, SeedIds.Service(2)));

        (await db.ShouldBeRejectedAsync(SqlState.UniqueViolation)).ShouldBe("ux_token_sequence_branch_date_priority_service");
    }

    [Fact]
    public async Task Normal_and_VIP_of_the_same_service_have_their_own_sequence()
    {
        // NB0001 and VB0001 both exist on the same day (spec 6).
        await using var db = await SeededAsync();
        db.TokenSequences.AddRange(Sequence(SeedIds.Vip, SeedIds.Service(2)), Sequence(SeedIds.Normal, SeedIds.Service(2)));

        (await db.SaveChangesAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task A_second_priority_type_with_the_same_prefix_in_a_company_is_rejected()
    {
        await using var db = await SeededAsync();
        db.PriorityTypes.Add(new PriorityType { CompanyId = SeedIds.Company1, NameEn = "Very VIP", NameAr = "مهم جدا", Prefix = "V", PriorityLevel = 1 });

        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
    }

    [Fact]
    public async Task A_second_service_type_with_the_same_prefix_in_a_company_is_rejected()
    {
        await using var db = await SeededAsync();
        db.ServiceTypes.Add(new ServiceType { CompanyId = SeedIds.Company1, NameEn = "Service 7", NameAr = "الخدمة 7", Prefix = "A" });

        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
    }

    [Fact]
    public async Task The_same_prefix_is_allowed_in_another_company()
    {
        await using var db = await SeededAsync();
        var other = new Company { NameEn = "Company 2", NameAr = "الشركة 2", Code = "C2", CurrencyCode = "SAR" };
        db.Companies.Add(other);
        db.ServiceTypes.Add(new ServiceType { CompanyId = other.Id, NameEn = "Service 1", NameAr = "الخدمة 1", Prefix = "A" });

        (await db.SaveChangesAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task A_second_token_with_the_same_number_on_the_same_day_in_a_branch_is_rejected()
    {
        await using var db = await SeededAsync();
        db.Tokens.Add(NewToken());
        await db.SaveChangesAsync();

        db.Tokens.Add(NewToken());

        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
    }

    [Fact]
    public async Task Mapping_tables_reject_duplicate_natural_keys()
    {
        await using var db = await SeededAsync();
        db.CompanyServicePriorities.Add(new CompanyServicePriority { CompanyId = SeedIds.Company1, ServiceTypeId = SeedIds.Service(1), PriorityTypeId = SeedIds.Normal });
        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
        db.ChangeTracker.Clear();

        db.BranchServicePriorities.Add(new BranchServicePriority
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, ServiceTypeId = SeedIds.Service(1), PriorityTypeId = SeedIds.Normal, MaxTokensPerDay = 5,
        });
        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
        db.ChangeTracker.Clear();

        db.CounterServicePriorities.Add(new CounterServicePriority
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, CounterId = SeedIds.Counter(1), ServiceTypeId = SeedIds.Service(1), PriorityTypeId = SeedIds.Normal,
        });
        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
        db.ChangeTracker.Clear();

        db.DispenserZoneServices.Add(new DispenserZoneService
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, DeviceId = SeedIds.Dispenser(1), ZoneId = SeedIds.Zone(1), ServiceTypeId = SeedIds.Service(1),
        });
        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
    }

    [Fact]
    public async Task Branch_priority_fee_is_unique_per_branch_and_priority_type()
    {
        await using var db = await SeededAsync();
        db.BranchPriorityFees.Add(new BranchPriorityFee { CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, PriorityTypeId = SeedIds.Vip, FeeAmount = 30m });
        await db.SaveChangesAsync();

        db.BranchPriorityFees.Add(new BranchPriorityFee { CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, PriorityTypeId = SeedIds.Vip, FeeAmount = 40m });

        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
    }

    [Fact]
    public async Task Counter_number_is_unique_per_branch()
    {
        await using var db = await SeededAsync();
        db.Counters.Add(new Counter { CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, ZoneId = SeedIds.Zone(1), Name = "Counter 1 again", Number = 1 });

        await db.ShouldBeRejectedAsync(SqlState.UniqueViolation);
    }

    // ---- AC-6 fee constraint

    [Theory]
    [InlineData(null)]
    [InlineData("-0.01")]
    [InlineData("-50")]
    public async Task A_paid_priority_type_without_a_valid_fee_is_rejected(string? fee)
    {
        await using var db = await SeededAsync();
        db.PriorityTypes.Add(new PriorityType
        {
            CompanyId = SeedIds.Company1, NameEn = "Gold", NameAr = "ذهبي", Prefix = "G", PriorityLevel = 1, IsPaid = true,
            FeeAmount = fee is null ? null : decimal.Parse(fee, System.Globalization.CultureInfo.InvariantCulture),
        });

        await db.ShouldBeRejectedAsync(SqlState.CheckViolation);
    }

    [Fact]
    public async Task Existing_VIP_cannot_be_updated_to_paid_without_a_fee()
    {
        await using var db = await SeededAsync();
        var vip = await db.PriorityTypes.IgnoreQueryFilters().SingleAsync(p => p.Prefix == "V");
        vip.FeeAmount = null;

        await db.ShouldBeRejectedAsync(SqlState.CheckViolation);
    }

    [Theory]
    [InlineData(true, "0")]
    [InlineData(true, "50.00")]
    [InlineData(false, null)]
    public async Task Valid_fee_combinations_are_accepted(bool isPaid, string? fee)
    {
        await using var db = await SeededAsync();
        db.PriorityTypes.Add(new PriorityType
        {
            CompanyId = SeedIds.Company1, NameEn = "Gold", NameAr = "ذهبي", Prefix = "G", PriorityLevel = 3, IsPaid = isPaid,
            FeeAmount = fee is null ? null : decimal.Parse(fee, System.Globalization.CultureInfo.InvariantCulture),
        });

        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Money_is_stored_with_two_decimals()
    {
        await using var db = await SeededAsync();
        var vip = await db.PriorityTypes.IgnoreQueryFilters().SingleAsync(p => p.Prefix == "V");
        vip.FeeAmount = 12.345m;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.PriorityTypes.IgnoreQueryFilters().SingleAsync(p => p.Prefix == "V")).FeeAmount.ShouldBe(12.35m);
    }

    // ---- other check constraints of STORY-002 section 3

    [Theory]
    [InlineData("a")]
    [InlineData("1")]
    public async Task Prefixes_must_be_one_upper_case_letter(string prefix)
    {
        await using var db = await SeededAsync();
        db.ServiceTypes.Add(new ServiceType { CompanyId = SeedIds.Company1, NameEn = "Bad", NameAr = "سيء", Prefix = prefix });

        await db.ShouldBeRejectedAsync(SqlState.CheckViolation);
    }

    [Fact]
    public async Task A_prefix_longer_than_one_letter_is_rejected()
    {
        await using var db = await SeededAsync();
        db.PriorityTypes.Add(new PriorityType { CompanyId = SeedIds.Company1, NameEn = "Bad", NameAr = "سيء", Prefix = "VV", PriorityLevel = 3 });

        var exception = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());
        exception.InnerException.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe("22001");
    }

    [Fact]
    public async Task Priority_level_must_be_at_least_1()
    {
        await using var db = await SeededAsync();
        db.PriorityTypes.Add(new PriorityType { CompanyId = SeedIds.Company1, NameEn = "Zero", NameAr = "صفر", Prefix = "Z", PriorityLevel = 0 });

        await db.ShouldBeRejectedAsync(SqlState.CheckViolation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10000)]
    public async Task Max_tokens_per_day_must_be_between_1_and_9999(int max)
    {
        await using var db = await SeededAsync();
        var pair = await db.BranchServicePriorities.IgnoreQueryFilters().FirstAsync();
        pair.MaxTokensPerDay = max;

        await db.ShouldBeRejectedAsync(SqlState.CheckViolation);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9999)]
    public async Task Max_tokens_per_day_accepts_the_bounds(int max)
    {
        await using var db = await SeededAsync();
        var pair = await db.BranchServicePriorities.IgnoreQueryFilters().FirstAsync();
        pair.MaxTokensPerDay = max;

        (await db.SaveChangesAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Token_sequence_is_limited_to_four_digits()
    {
        await using var db = await SeededAsync();
        var token = NewToken();
        token.Sequence = 10000;
        db.Tokens.Add(token);

        await db.ShouldBeRejectedAsync(SqlState.CheckViolation);
    }

    [Fact]
    public async Task A_fully_populated_paid_token_can_be_saved_and_read_back()
    {
        await using var db = await SeededAsync();
        var token = NewToken();
        token.FeeAmount = 50.00m;
        token.FeeStatus = FeeStatus.Due;
        db.Tokens.Add(token);
        db.TokenVisits.Add(new TokenVisit
        {
            CompanyId = SeedIds.Company1, BranchId = SeedIds.Branch1, TokenId = token.Id, ServiceTypeId = SeedIds.Service(2), PriorityTypeId = SeedIds.Vip, ZoneId = SeedIds.Zone(1),
            QueuedAt = DateTimeOffset.UtcNow, IsFront = false,
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var saved = await db.Tokens.IgnoreQueryFilters().SingleAsync(t => t.Number == "VB0001");
        (saved.FeeAmount, saved.FeeStatus, saved.Status).ShouldBe((50.00m, FeeStatus.Due, TokenStatus.Waiting));
    }
}
