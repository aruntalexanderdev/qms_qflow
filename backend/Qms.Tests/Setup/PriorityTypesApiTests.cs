using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Setup;

// STORY-005 AC-13 to AC-24 through the real host (Part B of TASK-010).
[Collection(PostgresCollection.Name)]
public sealed class PriorityTypesApiTests(PostgresFixture postgres)
{
    private const string Url = "/api/v1/priority-types";

    private static async Task<JsonElement> GetAsync(SetupHost setup, Guid id) =>
        (await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}")).Json.Clone();

    private static object Update(JsonElement current, string? nameEn = null, string? prefix = null, bool? isActive = null, bool? isPaid = null, decimal? fee = null, bool clearFee = false, string? version = null) =>
        new
        {
            name = nameEn ?? current.GetProperty("name").GetString(),
            translations = new { ar = current.GetProperty("translations").GetProperty("ar").GetString() },
            prefix = prefix ?? current.GetProperty("prefix").GetString(),
            priorityLevel = current.GetProperty("priorityLevel").GetInt32(),
            isPaid = isPaid ?? current.GetProperty("isPaid").GetBoolean(),
            feeAmount = clearFee ? null : fee ?? (current.GetProperty("feeAmount").ValueKind == JsonValueKind.Null ? (decimal?)null : current.GetProperty("feeAmount").GetDecimal()),
            isActive = isActive ?? current.GetProperty("isActive").GetBoolean(),
            version = version ?? current.GetProperty("version").GetString(),
        };

    // ---- AC-13, AC-14

    [Fact]
    public async Task Ac13_list_returns_vip_then_normal_with_the_company_currency()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);

        reply.Status.ShouldBe(200);
        var items = reply.Json.EnumerateArray().ToList();
        items.Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["V", "N"]);
        items[0].GetProperty("priorityLevel").GetInt32().ShouldBe(1);
        items[0].GetProperty("isPaid").GetBoolean().ShouldBeTrue();
        items[0].GetProperty("feeAmount").GetDecimal().ShouldBe(50.00m);
        items[1].GetProperty("priorityLevel").GetInt32().ShouldBe(2);
        items[1].GetProperty("isPaid").GetBoolean().ShouldBeFalse();
        items[1].GetProperty("feeAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        foreach (var item in items)
        {
            item.GetProperty("currencyCode").GetString().ShouldBe("AED");
            item.GetProperty("isActive").GetBoolean().ShouldBeTrue();
            item.GetProperty("version").GetString().ShouldNotBeNullOrEmpty();
        }

        reply.Body.ShouldNotContain("Normal X");
    }

    [Fact]
    public async Task Ac13_the_company_2_list_shows_only_its_own_row_with_its_own_currency()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.CompanyAdminC2, Url);

        var item = reply.Json.EnumerateArray().Single();
        item.GetProperty("name").GetString().ShouldBe("Normal X");
        item.GetProperty("currencyCode").GetString().ShouldBe("SAR");
    }

    [Fact]
    public async Task Ac13_the_isActive_filter_works_as_for_service_types_and_ties_sort_by_prefix()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var normal = await GetAsync(setup, SeedIds.Normal);
        (await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Normal}", Update(normal, isActive: false))).Status.ShouldBe(200);
        (await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Senior", translations = new { ar = "كبار السن" }, prefix = "S", priorityLevel = 1, isPaid = false })).Status.ShouldBe(201);

        var inactive = await setup.GetAsync(SetupUser.CompanyAdminC1, Url + "?isActive=false");
        var active = await setup.GetAsync(SetupUser.CompanyAdminC1, Url + "?isActive=true");
        var all = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);

        inactive.Json.EnumerateArray().Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["N"]);
        active.Json.EnumerateArray().Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["S", "V"]);
        all.Json.EnumerateArray().Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["S", "V", "N"]);
    }

    [Fact]
    public async Task Ac14_get_returns_the_fee_as_a_json_number()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Vip}");

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("feeAmount").ValueKind.ShouldBe(JsonValueKind.Number);
        reply.Json.GetProperty("feeAmount").GetDecimal().ShouldBe(50.00m);
        reply.Body.ShouldContain("\"feeAmount\":50.00");
        reply.Json.GetProperty("translations").GetProperty("ar").GetString().ShouldBe("كبار الشخصيات");
    }

    // ---- AC-15, AC-16

    [Fact]
    public async Task Ac15_create_an_unpaid_priority_type()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url,
            new { name = "Senior", translations = new { ar = "كبار السن" }, prefix = "S", priorityLevel = 1, isPaid = false, isActive = true, companyId = SetupTestData.Company2 });

        reply.Status.ShouldBe(201);
        reply.Response.Headers.Location!.ToString().ShouldBe($"/api/v1/priority-types/{SetupHost.Id(reply)}");
        reply.Json.GetProperty("feeAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        reply.Json.GetProperty("currencyCode").GetString().ShouldBe("AED");
        (await setup.GetAsync(SetupUser.CompanyAdminC2, Url)).Json.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Ac16_create_a_paid_priority_type_and_accept_a_zero_fee()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var paid = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Premium", translations = new { ar = "مميز" }, prefix = "P", priorityLevel = 1, isPaid = true, feeAmount = 75.50m });
        var free = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Gift", translations = new { ar = "هدية" }, prefix = "Q", priorityLevel = 1, isPaid = true, feeAmount = 0 });

        paid.Status.ShouldBe(201);
        paid.Json.GetProperty("isPaid").GetBoolean().ShouldBeTrue();
        paid.Json.GetProperty("feeAmount").GetDecimal().ShouldBe(75.50m);
        free.Status.ShouldBe(201);
        free.Json.GetProperty("feeAmount").GetDecimal().ShouldBe(0m);
    }

    // ---- AC-17

    [Fact]
    public async Task Ac17_every_invalid_body_is_400_validation_failed_naming_the_field_and_saves_nothing()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var cases = new (string Field, object Body)[]
        {
            ("name", new { name = "", translations = new { ar = "ع" }, prefix = "P", priorityLevel = 1, isPaid = false }),
            ("prefix", new { name = "x", translations = new { ar = "ع" }, prefix = "p", priorityLevel = 1, isPaid = false }),
            ("prefix", new { name = "x", translations = new { ar = "ع" }, prefix = "PV", priorityLevel = 1, isPaid = false }),
            ("prefix", new { name = "x", translations = new { ar = "ع" }, prefix = "7", priorityLevel = 1, isPaid = false }),
            ("prefix", new { name = "x", translations = new { ar = "ع" }, prefix = "", priorityLevel = 1, isPaid = false }),
            ("priorityLevel", new { name = "x", translations = new { ar = "ع" }, prefix = "P", priorityLevel = 0, isPaid = false }),
            ("priorityLevel", new { name = "x", translations = new { ar = "ع" }, prefix = "P", priorityLevel = -1, isPaid = false }),
            ("feeAmount", new { name = "x", translations = new { ar = "ع" }, prefix = "P", priorityLevel = 1, isPaid = true }),
            ("feeAmount", new { name = "x", translations = new { ar = "ع" }, prefix = "P", priorityLevel = 1, isPaid = true, feeAmount = 12.345m }),
            ("feeAmount", new { name = "x", translations = new { ar = "ع" }, prefix = "P", priorityLevel = 1, isPaid = true, feeAmount = -1 }),
            ("feeAmount", new { name = "x", translations = new { ar = "ع" }, prefix = "P", priorityLevel = 1, isPaid = false, feeAmount = 10 }),
            ("feeAmount", new { name = "x", translations = new { ar = "ع" }, prefix = "P", priorityLevel = 1, isPaid = false, feeAmount = 0 }),
            ("feeAmount", new { name = "x", translations = new { ar = "ع" }, prefix = "P", priorityLevel = 1, isPaid = true, feeAmount = 10000000000m }),
        };

        foreach (var (field, body) in cases)
        {
            var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, body);

            reply.Status.ShouldBe(400, JsonSerializer.Serialize(body));
            reply.Code.ShouldBe("validation.failed");
            reply.Json.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue($"{field} in {reply.Body}");
        }

        (await setup.GetAsync(SetupUser.CompanyAdminC1, Url)).Json.GetArrayLength().ShouldBe(2);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Ac17_the_largest_fee_with_two_decimals_is_accepted()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url,
            new { name = "Max", translations = new { ar = "أقصى" }, prefix = "M", priorityLevel = 1, isPaid = true, feeAmount = 9999999999.99m });

        reply.Status.ShouldBe(201);
        reply.Json.GetProperty("feeAmount").GetDecimal().ShouldBe(9999999999.99m);
    }

    [Fact]
    public async Task Translations_other_than_arabic_are_refused_for_now_and_arabic_is_optional()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var french = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Senior", translations = new { fr = "Aîné" }, prefix = "S", priorityLevel = 1, isPaid = false });
        var none = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Senior", prefix = "S", priorityLevel = 1, isPaid = false });

        french.Status.ShouldBe(400);
        french.Code.ShouldBe("translations.unsupported_language");
        none.Status.ShouldBe(201);
        none.Json.GetProperty("translations").EnumerateObject().ShouldBeEmpty();
        none.Body.ShouldNotContain("nameEn");
    }

    // ---- AC-18

    [Fact]
    public async Task Ac18_prefix_is_unique_per_company_but_independent_of_service_type_prefixes()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var normal = await GetAsync(setup, SeedIds.Normal);

        var create = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Other", translations = new { ar = "آخر" }, prefix = "V", priorityLevel = 3, isPaid = false });
        var update = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Normal}", Update(normal, prefix: "V"));
        var sameLetterAsService = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Alpha", translations = new { ar = "ألفا" }, prefix = "A", priorityLevel = 3, isPaid = false });
        var serviceWithPriorityLetter = await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { name = "Vee", translations = new { ar = "في" }, prefix = "V" });
        var otherCompany = await setup.PostAsync(SetupUser.CompanyAdminC2, Url, new { name = "Vip X", translations = new { ar = "في آي بي" }, prefix = "V", priorityLevel = 1, isPaid = false });

        create.Status.ShouldBe(409);
        create.Code.ShouldBe("priority_type.prefix_taken");
        update.Status.ShouldBe(409);
        update.Code.ShouldBe("priority_type.prefix_taken");
        sameLetterAsService.Status.ShouldBe(201);
        serviceWithPriorityLetter.Status.ShouldBe(201);
        otherCompany.Status.ShouldBe(201);
    }

    [Fact]
    public async Task Ac18_two_concurrent_creates_of_the_same_prefix_give_one_201_and_one_409()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        object Body(string name) => new { name, translations = new { ar = "ع" }, prefix = "S", priorityLevel = 1, isPaid = false };

        var replies = await Task.WhenAll(setup.PostAsync(SetupUser.CompanyAdminC1, Url, Body("One")), setup.PostAsync(SetupUser.CompanyAdminC1, Url, Body("Two")));

        replies.Select(r => r.Status).Order().ToArray().ShouldBe(new[] { 201, 409 });
        replies.Single(r => r.Status == 409).Code.ShouldBe("priority_type.prefix_taken");
    }

    // ---- AC-19, AC-20

    [Fact]
    public async Task Ac19_update_the_fee_of_vip()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var vip = await GetAsync(setup, SeedIds.Vip);

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Vip}", Update(vip, isPaid: true, fee: 60.00m));

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("feeAmount").GetDecimal().ShouldBe(60.00m);
        reply.Json.GetProperty("version").GetString().ShouldNotBe(vip.GetProperty("version").GetString());
        (await GetAsync(setup, SeedIds.Vip)).GetProperty("feeAmount").GetDecimal().ShouldBe(60.00m);
    }

    [Fact]
    public async Task Ac20_paid_to_unpaid_and_back_keeps_branch_fee_overrides()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        await setup.AddBranchFeeAsync(SeedIds.Vip);
        var vip = await GetAsync(setup, SeedIds.Vip);

        var withFee = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Vip}", Update(vip, isPaid: false, fee: 50m));
        var unpaid = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Vip}", Update(vip, isPaid: false, clearFee: true));
        var paidAgain = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Vip}", Update(unpaid.Json, isPaid: true, fee: 55m));

        withFee.Status.ShouldBe(400);
        withFee.Json.GetProperty("errors").TryGetProperty("feeAmount", out _).ShouldBeTrue();
        unpaid.Status.ShouldBe(200);
        unpaid.Json.GetProperty("isPaid").GetBoolean().ShouldBeFalse();
        unpaid.Json.GetProperty("feeAmount").ValueKind.ShouldBe(JsonValueKind.Null);
        paidAgain.Status.ShouldBe(200);
        paidAgain.Json.GetProperty("feeAmount").GetDecimal().ShouldBe(55m);
        await using var db = setup.Host.OpenContext();
        db.BranchPriorityFees.IgnoreQueryFilters().Count(f => f.PriorityTypeId == SeedIds.Vip).ShouldBe(1);
    }

    // ---- AC-21, AC-22

    [Fact]
    public async Task Ac21_a_stale_version_is_409_and_a_missing_version_is_400()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var v1 = await GetAsync(setup, SeedIds.Normal);

        var first = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Normal}", Update(v1, nameEn: "First"));
        var second = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Normal}", Update(v1, nameEn: "Second"));
        var missing = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Normal}",
            new { name = "x", translations = new { ar = "ع" }, prefix = "N", priorityLevel = 2, isPaid = false, isActive = true });

        first.Status.ShouldBe(200);
        second.Status.ShouldBe(409);
        second.Code.ShouldBe("request.conflict");
        missing.Status.ShouldBe(400);
        missing.Code.ShouldBe("validation.failed");
        missing.Json.GetProperty("errors").TryGetProperty("version", out _).ShouldBeTrue();
        (await GetAsync(setup, SeedIds.Normal)).GetProperty("name").GetString().ShouldBe("First");
    }

    [Fact]
    public async Task Ac22_deactivate_a_priority_type()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var normal = await GetAsync(setup, SeedIds.Normal);

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Normal}", Update(normal, isActive: false));

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        (await setup.GetAsync(SetupUser.CompanyAdminC1, Url)).Json.GetArrayLength().ShouldBe(2);
        (await setup.GetAsync(SetupUser.CompanyAdminC1, Url + "?isActive=false")).Json.EnumerateArray().Single().GetProperty("prefix").GetString().ShouldBe("N");
    }

    // ---- AC-23, AC-24

    [Fact]
    public async Task Ac23_an_unused_priority_type_is_deleted_with_204_and_an_audit_row()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var id = SetupHost.Id(await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Senior", translations = new { ar = "كبار السن" }, prefix = "S", priorityLevel = 1, isPaid = false }));

        var reply = await setup.DeleteAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}");

        reply.Status.ShouldBe(204);
        (await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}")).Code.ShouldBe("priority_type.not_found");
        var audit = (await setup.AuditRowsAsync()).Last();
        (audit.EntityName, audit.Action, audit.EntityId).ShouldBe(("priority_type", "delete", id));
    }

    [Fact]
    public async Task Ac23_vip_in_the_company_mapping_cannot_be_deleted()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.DeleteAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Vip}");

        reply.Status.ShouldBe(409);
        reply.Code.ShouldBe("priority_type.in_use");
        (await setup.PriorityTypeExistsAsync(SeedIds.Vip)).ShouldBeTrue();
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("company mapping")]
    [InlineData("branch fee")]
    [InlineData("token")]
    [InlineData("token sequence")]
    public async Task Ac23_a_single_reference_of_each_kind_blocks_the_delete(string kind)
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var id = SetupHost.Id(await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { name = "Senior", translations = new { ar = "كبار السن" }, prefix = "S", priorityLevel = 1, isPaid = false }));
        switch (kind)
        {
            case "company mapping":
                await setup.AddCompanyMappingAsync(SeedIds.Service(6), id);
                break;
            case "branch fee":
                await setup.AddBranchFeeAsync(id);
                break;
            case "token":
                await setup.AddTokenAsync(SeedIds.Service(1), id);
                break;
            default:
                await setup.AddTokenSequenceAsync(SeedIds.Service(1), id);
                break;
        }

        var reply = await setup.DeleteAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}");

        reply.Status.ShouldBe(409, kind);
        reply.Code.ShouldBe("priority_type.in_use");
        (await setup.PriorityTypeExistsAsync(id)).ShouldBeTrue();
        (await setup.AuditRowsAsync()).ShouldNotContain(a => a.Action == "delete");
    }

    [Fact]
    public async Task Ac24_another_company_gets_404_priority_type_not_found_exactly_like_an_unknown_id()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var foreign = $"{Url}/{SeedIds.Vip}";
        var unknown = $"{Url}/{Guid.NewGuid()}";
        var body = new { name = "Hacked", translations = new { ar = "ع" }, prefix = "V", priorityLevel = 1, isPaid = true, feeAmount = 1, isActive = true, version = "AAAAAA" };

        var results = new List<(ApiReply Foreign, ApiReply Unknown)>
        {
            (await setup.GetAsync(SetupUser.CompanyAdminC2, foreign), await setup.GetAsync(SetupUser.CompanyAdminC2, unknown)),
            (await setup.PutAsync(SetupUser.CompanyAdminC2, foreign, body), await setup.PutAsync(SetupUser.CompanyAdminC2, unknown, body)),
            (await setup.DeleteAsync(SetupUser.CompanyAdminC2, foreign), await setup.DeleteAsync(SetupUser.CompanyAdminC2, unknown)),
        };

        foreach (var (foreignReply, unknownReply) in results)
        {
            foreignReply.Status.ShouldBe(404);
            foreignReply.Code.ShouldBe("priority_type.not_found");
            unknownReply.Status.ShouldBe(404);
            unknownReply.Code.ShouldBe("priority_type.not_found");
            foreignReply.Json.GetProperty("detail").GetString().ShouldBe(unknownReply.Json.GetProperty("detail").GetString());
        }

        (await GetAsync(setup, SeedIds.Vip)).GetProperty("feeAmount").GetDecimal().ShouldBe(50.00m);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }
}
