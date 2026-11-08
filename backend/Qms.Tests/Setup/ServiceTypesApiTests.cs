using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Setup;

// STORY-005 AC-1 to AC-12 through the real host (Part B of TASK-010).
[Collection(PostgresCollection.Name)]
public sealed class ServiceTypesApiTests(PostgresFixture postgres)
{
    private const string Url = "/api/v1/service-types";

    private static object NewBody(string prefix, string nameEn = "Cards", string nameAr = "البطاقات", int? targetMinutes = 10, int sortOrder = 7, bool isActive = true) =>
        new { nameEn, nameAr, prefix, targetMinutes, sortOrder, isActive };

    private static object UpdateBody(JsonElement current, string? nameEn = null, string? nameAr = null, string? prefix = null, int? targetMinutes = null, int? sortOrder = null, bool? isActive = null, string? version = null) =>
        new
        {
            nameEn = nameEn ?? current.GetProperty("nameEn").GetString(),
            nameAr = nameAr ?? current.GetProperty("nameAr").GetString(),
            prefix = prefix ?? current.GetProperty("prefix").GetString(),
            targetMinutes = targetMinutes ?? (current.GetProperty("targetMinutes").ValueKind == JsonValueKind.Null ? (int?)null : current.GetProperty("targetMinutes").GetInt32()),
            sortOrder = sortOrder ?? current.GetProperty("sortOrder").GetInt32(),
            isActive = isActive ?? current.GetProperty("isActive").GetBoolean(),
            version = version ?? current.GetProperty("version").GetString(),
        };

    private static async Task<JsonElement> GetAsync(SetupHost setup, Guid id) =>
        (await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}")).Json.Clone();

    // ---- AC-1, AC-2, AC-3

    [Fact]
    public async Task Ac1_list_returns_the_six_seeded_service_types_in_order_without_company_2_rows()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);

        reply.Status.ShouldBe(200);
        var items = reply.Json.EnumerateArray().ToList();
        items.Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["A", "B", "C", "D", "F", "G"]);
        items.Select(i => i.GetProperty("sortOrder").GetInt32()).ShouldBe([1, 2, 3, 4, 5, 6]);
        foreach (var item in items)
        {
            item.GetProperty("id").GetGuid().ShouldNotBe(Guid.Empty);
            item.GetProperty("nameEn").GetString().ShouldNotBeNullOrEmpty();
            item.GetProperty("nameAr").GetString().ShouldNotBeNullOrEmpty();
            item.GetProperty("isActive").GetBoolean().ShouldBeTrue();
            item.GetProperty("version").GetString().ShouldNotBeNullOrEmpty();
            item.TryGetProperty("targetMinutes", out _).ShouldBeTrue();
            item.TryGetProperty("companyId", out _).ShouldBeFalse();
        }

        reply.Body.ShouldNotContain("Service X");
    }

    [Fact]
    public async Task Ac1_equal_sort_orders_fall_back_to_the_english_name()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        (await setup.PostAsync(SetupUser.CompanyAdminC1, Url, NewBody("H", nameEn: "Alpha", sortOrder: 6))).Status.ShouldBe(201);

        var names = (await setup.GetAsync(SetupUser.CompanyAdminC1, Url)).Json.EnumerateArray()
            .Where(i => i.GetProperty("sortOrder").GetInt32() == 6).Select(i => i.GetProperty("nameEn").GetString()).ToList();

        names.ShouldBe(["Alpha", "Service 6"]);
    }

    [Fact]
    public async Task Ac2_the_isActive_filter_returns_the_inactive_or_the_active_rows()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var service6 = await GetAsync(setup, SeedIds.Service(6));
        (await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(6)}", UpdateBody(service6, isActive: false))).Status.ShouldBe(200);

        var inactive = await setup.GetAsync(SetupUser.CompanyAdminC1, Url + "?isActive=false");
        var active = await setup.GetAsync(SetupUser.CompanyAdminC1, Url + "?isActive=true");
        var all = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);

        inactive.Json.EnumerateArray().Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["G"]);
        active.Json.EnumerateArray().Select(i => i.GetProperty("prefix").GetString()).ShouldBe(["A", "B", "C", "D", "F"]);
        all.Json.GetArrayLength().ShouldBe(6);
    }

    [Fact]
    public async Task Ac3_get_returns_one_service_type_with_its_arabic_name_unchanged()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(2)}");

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("prefix").GetString().ShouldBe("B");
        reply.Json.GetProperty("nameAr").GetString().ShouldBe("الخدمة 2");
        reply.Json.GetProperty("nameEn").GetString().ShouldBe("Service 2");
    }

    // ---- AC-4, AC-5

    [Fact]
    public async Task Ac4_create_returns_201_with_location_and_the_company_comes_from_the_token()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new
        {
            nameEn = "Cards", nameAr = "البطاقات", prefix = "H", targetMinutes = 10, sortOrder = 7, isActive = true,
            companyId = SetupTestData.Company2,
        });

        reply.Status.ShouldBe(201);
        var id = SetupHost.Id(reply);
        reply.Response.Headers.Location!.ToString().ShouldBe($"/api/v1/service-types/{id}");
        reply.Json.GetProperty("prefix").GetString().ShouldBe("H");
        reply.Json.GetProperty("targetMinutes").GetInt32().ShouldBe(10);
        reply.Json.GetProperty("sortOrder").GetInt32().ShouldBe(7);
        reply.Json.GetProperty("version").GetString().ShouldNotBeNullOrEmpty();
        await using (var db = setup.Host.OpenContext())
        {
            (await db.ServiceTypes.IgnoreQueryFilters().SingleAsync(s => s.Id == id)).CompanyId.ShouldBe(SeedIds.Company1);
        }

        var read = await setup.GetAsync(SetupUser.CompanyAdminC1, reply.Response.Headers.Location.ToString());
        read.Status.ShouldBe(200);
        read.Json.GetProperty("nameAr").GetString().ShouldBe("البطاقات");
    }

    [Fact]
    public async Task Ac5_every_invalid_body_is_400_validation_failed_naming_the_field_and_saves_nothing()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var cases = new (string Field, object Body)[]
        {
            ("nameEn", new { nameEn = "", nameAr = "ع", prefix = "H" }),
            ("nameEn", new { nameEn = "   ", nameAr = "ع", prefix = "H" }),
            ("nameAr", new { nameEn = "x", prefix = "H" }),
            ("nameEn", new { nameEn = new string('x', 201), nameAr = "ع", prefix = "H" }),
            ("nameAr", new { nameEn = "x", nameAr = new string('ع', 201), prefix = "H" }),
            ("prefix", new { nameEn = "x", nameAr = "ع", prefix = "h" }),
            ("prefix", new { nameEn = "x", nameAr = "ع", prefix = "HB" }),
            ("prefix", new { nameEn = "x", nameAr = "ع", prefix = "1" }),
            ("prefix", new { nameEn = "x", nameAr = "ع", prefix = "" }),
            ("prefix", new { nameEn = "x", nameAr = "ع", prefix = "  " }),
            ("prefix", new { nameEn = "x", nameAr = "ع" }),
            ("targetMinutes", new { nameEn = "x", nameAr = "ع", prefix = "H", targetMinutes = 0 }),
            ("targetMinutes", new { nameEn = "x", nameAr = "ع", prefix = "H", targetMinutes = -5 }),
            ("sortOrder", new { nameEn = "x", nameAr = "ع", prefix = "H", sortOrder = -1 }),
        };

        foreach (var (field, body) in cases)
        {
            var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, body);

            reply.Status.ShouldBe(400, JsonSerializer.Serialize(body));
            reply.Code.ShouldBe("validation.failed");
            reply.Json.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue($"{field} in {reply.Body}");
        }

        (await setup.GetAsync(SetupUser.CompanyAdminC1, Url)).Json.GetArrayLength().ShouldBe(6);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Ac5_a_null_target_is_accepted_and_a_padded_prefix_is_trimmed()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, new { nameEn = "  Cards  ", nameAr = " البطاقات ", prefix = " H " });

        reply.Status.ShouldBe(201);
        reply.Json.GetProperty("prefix").GetString().ShouldBe("H");
        reply.Json.GetProperty("nameEn").GetString().ShouldBe("Cards");
        reply.Json.GetProperty("nameAr").GetString().ShouldBe("البطاقات");
        reply.Json.GetProperty("targetMinutes").ValueKind.ShouldBe(JsonValueKind.Null);
        reply.Json.GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    // ---- AC-6

    [Fact]
    public async Task Ac6_a_prefix_is_unique_per_company_on_create_and_on_update()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var create = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, NewBody("A"));
        var service2 = await GetAsync(setup, SeedIds.Service(2));
        var update = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(2)}", UpdateBody(service2, prefix: "A"));

        create.Status.ShouldBe(409);
        create.Code.ShouldBe("service_type.prefix_taken");
        update.Status.ShouldBe(409);
        update.Code.ShouldBe("service_type.prefix_taken");
        (await GetAsync(setup, SeedIds.Service(2))).GetProperty("prefix").GetString().ShouldBe("B");
    }

    [Fact]
    public async Task Ac6_prefixes_are_independent_per_company()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var free = await setup.PostAsync(SetupUser.CompanyAdminC2, Url, NewBody("B"));
        var taken = await setup.PostAsync(SetupUser.CompanyAdminC2, Url, NewBody("A"));

        free.Status.ShouldBe(201);
        taken.Status.ShouldBe(409);
        taken.Code.ShouldBe("service_type.prefix_taken");
    }

    [Fact]
    public async Task Ac6_two_concurrent_creates_of_the_same_prefix_give_one_201_and_one_409()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        foreach (var prefix in new[] { "H", "J", "K" })
        {
            var replies = await Task.WhenAll(
                setup.PostAsync(SetupUser.CompanyAdminC1, Url, NewBody(prefix, "One")),
                setup.PostAsync(SetupUser.CompanyAdminC1, Url, NewBody(prefix, "Two")));

            replies.Select(r => r.Status).Order().ToArray().ShouldBe(new[] { 201, 409 }, prefix);
            replies.Single(r => r.Status == 409).Code.ShouldBe("service_type.prefix_taken");
        }

        // The loser rolled back: one audit row per winner.
        (await setup.AuditCountAsync()).ShouldBe(3);
    }

    // ---- AC-7, AC-8, AC-9

    [Fact]
    public async Task Ac7_update_replaces_all_fields_and_returns_a_new_version()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var before = await GetAsync(setup, SeedIds.Service(3));

        var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(3)}",
            new { nameEn = "Loans", nameAr = "القروض", prefix = "C", targetMinutes = 20, sortOrder = 3, isActive = true, version = before.GetProperty("version").GetString() });

        reply.Status.ShouldBe(200);
        reply.Json.GetProperty("nameEn").GetString().ShouldBe("Loans");
        reply.Json.GetProperty("targetMinutes").GetInt32().ShouldBe(20);
        reply.Json.GetProperty("version").GetString().ShouldNotBe(before.GetProperty("version").GetString());
        var after = await GetAsync(setup, SeedIds.Service(3));
        after.GetProperty("nameAr").GetString().ShouldBe("القروض");
        after.GetProperty("version").GetString().ShouldBe(reply.Json.GetProperty("version").GetString());
    }

    [Fact]
    public async Task Ac8_a_stale_version_is_409_request_conflict_and_the_first_change_stays()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var v1 = await GetAsync(setup, SeedIds.Service(3));

        var first = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(3)}", UpdateBody(v1, nameEn: "First"));
        var second = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(3)}", UpdateBody(v1, nameEn: "Second"));

        first.Status.ShouldBe(200);
        second.Status.ShouldBe(409);
        second.Code.ShouldBe("request.conflict");
        (await GetAsync(setup, SeedIds.Service(3))).GetProperty("nameEn").GetString().ShouldBe("First");
        (await setup.AuditRowsAsync()).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Ac8_a_stale_version_conflicts_even_when_the_values_are_unchanged()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var v1 = await GetAsync(setup, SeedIds.Service(3));
        (await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(3)}", UpdateBody(v1, nameEn: "First"))).Status.ShouldBe(200);

        var again = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(3)}", UpdateBody(v1, nameEn: "First"));

        again.Status.ShouldBe(409);
        again.Code.ShouldBe("request.conflict");
    }

    [Fact]
    public async Task Ac8_a_missing_empty_or_unreadable_version_is_400_with_an_error_on_version()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var bodies = new object[]
        {
            new { nameEn = "x", nameAr = "ع", prefix = "C", sortOrder = 3, isActive = true },
            new { nameEn = "x", nameAr = "ع", prefix = "C", sortOrder = 3, isActive = true, version = "" },
            new { nameEn = "x", nameAr = "ع", prefix = "C", sortOrder = 3, isActive = true, version = "   " },
            new { nameEn = "x", nameAr = "ع", prefix = "C", sortOrder = 3, isActive = true, version = "not-a-version" },
            new { nameEn = "x", nameAr = "ع", prefix = "C", sortOrder = 3, isActive = true, version = "12345678901234567890" },
        };

        foreach (var body in bodies)
        {
            var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(3)}", body);

            reply.Status.ShouldBe(400, JsonSerializer.Serialize(body));
            reply.Code.ShouldBe("validation.failed");
            reply.Json.GetProperty("errors").TryGetProperty("version", out _).ShouldBeTrue(reply.Body);
        }
    }

    [Fact]
    public async Task Ac9_a_service_type_in_the_company_mapping_can_be_deactivated_and_reactivated()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var service6 = await GetAsync(setup, SeedIds.Service(6));

        var off = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(6)}", UpdateBody(service6, isActive: false));
        var stillListed = await setup.GetAsync(SetupUser.CompanyAdminC1, Url);
        var on = await setup.PutAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(6)}", UpdateBody(off.Json, isActive: true));

        off.Status.ShouldBe(200);
        off.Json.GetProperty("isActive").GetBoolean().ShouldBeFalse();
        SetupHost.Item(stillListed, "G").GetProperty("isActive").GetBoolean().ShouldBeFalse();
        on.Status.ShouldBe(200);
        on.Json.GetProperty("isActive").GetBoolean().ShouldBeTrue();
    }

    // ---- AC-10, AC-11

    [Fact]
    public async Task Ac10_an_unused_service_type_is_deleted_with_204_and_an_audit_row()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var created = await setup.PostAsync(SetupUser.CompanyAdminC1, Url, NewBody("H"));
        var id = SetupHost.Id(created);

        var reply = await setup.DeleteAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}");

        reply.Status.ShouldBe(204);
        reply.Body.ShouldBeEmpty();
        var read = await setup.GetAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}");
        read.Status.ShouldBe(404);
        read.Code.ShouldBe("service_type.not_found");
        var audit = (await setup.AuditRowsAsync()).Last();
        (audit.EntityName, audit.Action, audit.EntityId).ShouldBe(("service_type", "delete", id));
    }

    [Fact]
    public async Task Ac11_a_service_type_in_the_company_mapping_cannot_be_deleted()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        var reply = await setup.DeleteAsync(SetupUser.CompanyAdminC1, $"{Url}/{SeedIds.Service(1)}");

        reply.Status.ShouldBe(409);
        reply.Code.ShouldBe("service_type.in_use");
        (await setup.ServiceTypeExistsAsync(SeedIds.Service(1))).ShouldBeTrue();
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData("flow")]
    [InlineData("dispenser mapping")]
    [InlineData("token")]
    [InlineData("token sequence")]
    public async Task Ac11_a_single_reference_of_each_kind_blocks_the_delete(string kind)
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var id = SetupHost.Id(await setup.PostAsync(SetupUser.CompanyAdminC1, Url, NewBody("H")));
        switch (kind)
        {
            case "flow":
                await setup.AddFlowAsync(id, SeedIds.Service(1));
                break;
            case "dispenser mapping":
                await setup.AddDispenserMappingAsync(id);
                break;
            case "token":
                await setup.AddTokenAsync(id, SeedIds.Normal);
                break;
            default:
                await setup.AddTokenSequenceAsync(id, SeedIds.Normal);
                break;
        }

        var reply = await setup.DeleteAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}");

        reply.Status.ShouldBe(409, kind);
        reply.Code.ShouldBe("service_type.in_use");
        (await setup.ServiceTypeExistsAsync(id)).ShouldBeTrue();
        (await setup.AuditRowsAsync()).ShouldNotContain(a => a.Action == "delete");
    }

    [Fact]
    public async Task Ac11_the_target_of_a_flow_blocks_the_delete_too()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var id = SetupHost.Id(await setup.PostAsync(SetupUser.CompanyAdminC1, Url, NewBody("H")));
        await setup.AddFlowAsync(SeedIds.Service(1), id);

        var reply = await setup.DeleteAsync(SetupUser.CompanyAdminC1, $"{Url}/{id}");

        reply.Status.ShouldBe(409);
        reply.Code.ShouldBe("service_type.in_use");
    }

    // ---- AC-12

    [Fact]
    public async Task Ac12_another_company_gets_404_service_type_not_found_exactly_like_an_unknown_id()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var foreign = $"{Url}/{SeedIds.Service(2)}";
        var unknown = $"{Url}/{Guid.NewGuid()}";
        var body = new { nameEn = "Hacked", nameAr = "ع", prefix = "B", sortOrder = 2, isActive = true, version = "AAAAAA" };

        var results = new List<(ApiReply Foreign, ApiReply Unknown)>
        {
            (await setup.GetAsync(SetupUser.CompanyAdminC2, foreign), await setup.GetAsync(SetupUser.CompanyAdminC2, unknown)),
            (await setup.PutAsync(SetupUser.CompanyAdminC2, foreign, body), await setup.PutAsync(SetupUser.CompanyAdminC2, unknown, body)),
            (await setup.DeleteAsync(SetupUser.CompanyAdminC2, foreign), await setup.DeleteAsync(SetupUser.CompanyAdminC2, unknown)),
        };

        foreach (var (foreignReply, unknownReply) in results)
        {
            foreignReply.Status.ShouldBe(404);
            foreignReply.Code.ShouldBe("service_type.not_found");
            unknownReply.Status.ShouldBe(404);
            unknownReply.Code.ShouldBe("service_type.not_found");
            foreignReply.Json.GetProperty("title").GetString().ShouldBe(unknownReply.Json.GetProperty("title").GetString());
            foreignReply.Json.GetProperty("detail").GetString().ShouldBe(unknownReply.Json.GetProperty("detail").GetString());
        }

        (await GetAsync(setup, SeedIds.Service(2))).GetProperty("nameEn").GetString().ShouldBe("Service 2");
        var c2List = await setup.GetAsync(SetupUser.CompanyAdminC2, Url);
        c2List.Json.EnumerateArray().Select(i => i.GetProperty("nameEn").GetString()).ShouldBe(["Service X"]);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }
}
