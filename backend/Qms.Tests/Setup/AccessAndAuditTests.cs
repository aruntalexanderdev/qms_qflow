using System.Text.Json;
using Qms.Infrastructure.Persistence.Seed;
using Qms.Tests.Persistence;
using Qms.Tests.Support;

namespace Qms.Tests.Setup;

// STORY-005 AC-25 (role matrix), AC-26 and AC-27 (audit), AC-33 (device notifier), AC-37 (error format) through the real host.
[Collection(PostgresCollection.Name)]
public sealed class AccessAndAuditTests(PostgresFixture postgres)
{
    private static readonly Guid Service1 = SeedIds.Service(1);

    private static (HttpMethod Method, string Url, object? Body, bool IsWrite)[] Endpoints() =>
    [
        (HttpMethod.Get, "/api/v1/service-types", null, false),
        (HttpMethod.Get, $"/api/v1/service-types/{Service1}", null, false),
        (HttpMethod.Post, "/api/v1/service-types", new { nameEn = "Cards", nameAr = "ب", prefix = "H" }, true),
        (HttpMethod.Put, $"/api/v1/service-types/{Service1}", new { nameEn = "Z", nameAr = "ز", prefix = "A", sortOrder = 1, isActive = true, version = "AAAAAA" }, true),
        (HttpMethod.Delete, $"/api/v1/service-types/{Service1}", null, true),
        (HttpMethod.Get, "/api/v1/priority-types", null, false),
        (HttpMethod.Get, $"/api/v1/priority-types/{SeedIds.Vip}", null, false),
        (HttpMethod.Post, "/api/v1/priority-types", new { nameEn = "S", nameAr = "س", prefix = "S", priorityLevel = 1, isPaid = false }, true),
        (HttpMethod.Put, $"/api/v1/priority-types/{SeedIds.Vip}", new { nameEn = "Z", nameAr = "ز", prefix = "V", priorityLevel = 1, isPaid = true, feeAmount = 1, isActive = true, version = "AAAAAA" }, true),
        (HttpMethod.Delete, $"/api/v1/priority-types/{SeedIds.Vip}", null, true),
    ];

    // ---- AC-25

    [Fact]
    public async Task Ac25_company_admin_reads_and_branch_managers_read_but_cannot_write()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        foreach (var (method, url, body, isWrite) in Endpoints())
        {
            var admin = await setup.CallAsync(SetupUser.CompanyAdminC1, method, url, body);
            if (!isWrite)
            {
                admin.Status.ShouldBe(200, $"{method} {url} as CA");
            }

            foreach (var manager in new[] { SetupUser.BranchManagerB1, SetupUser.BranchManagerB2 })
            {
                var reply = await setup.CallAsync(manager, method, url, body);
                reply.Status.ShouldBe(isWrite ? 403 : 200, $"{method} {url} as {manager}");
                if (isWrite)
                {
                    reply.Code.ShouldBe("auth.forbidden");
                }
            }
        }
    }

    [Fact]
    public async Task Ac25_branch_manager_writes_change_nothing()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        foreach (var (method, url, body, isWrite) in Endpoints().Where(e => e.IsWrite))
        {
            (await setup.CallAsync(SetupUser.BranchManagerB1, method, url, body)).Status.ShouldBe(403);
        }

        (await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types")).Json.GetArrayLength().ShouldBe(6);
        (await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/priority-types")).Json.GetArrayLength().ShouldBe(2);
        (await setup.AuditCountAsync()).ShouldBe(0);
    }

    [Theory]
    [InlineData(SetupUser.CounterStaff)]
    [InlineData(SetupUser.SuperAdmin)]
    public async Task Ac25_counter_staff_and_super_admin_get_403_on_all_ten_endpoints(SetupUser user)
    {
        using var setup = await SetupHost.StartAsync(postgres);

        foreach (var (method, url, body, _) in Endpoints())
        {
            var reply = await setup.CallAsync(user, method, url, body);

            reply.Status.ShouldBe(403, $"{method} {url}");
            reply.Code.ShouldBe("auth.forbidden");
        }
    }

    [Fact]
    public async Task Ac25_without_a_token_every_endpoint_is_401()
    {
        using var setup = await SetupHost.StartAsync(postgres);

        foreach (var (method, url, body, _) in Endpoints())
        {
            var reply = await setup.CallAsync(null, method, url, body);

            reply.Status.ShouldBe(401, $"{method} {url}");
            reply.Code.ShouldBe("auth.unauthorized");
        }
    }

    // ---- AC-26, AC-27

    [Fact]
    public async Task Ac26_create_update_and_delete_write_three_audit_rows_in_order_and_reads_write_none()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var vip = (await setup.GetAsync(SetupUser.CompanyAdminC1, $"/api/v1/priority-types/{SeedIds.Vip}")).Json.Clone();
        var version = vip.GetProperty("version").GetString();

        var created = await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { nameEn = "Cards", nameAr = "البطاقات", prefix = "H", targetMinutes = 10, sortOrder = 7, isActive = true });
        var cardsId = SetupHost.Id(created);
        (await setup.PutAsync(SetupUser.CompanyAdminC1, $"/api/v1/priority-types/{SeedIds.Vip}",
            new { nameEn = "VIP", nameAr = "كبار الشخصيات", prefix = "V", priorityLevel = 1, isPaid = true, feeAmount = 60.00m, isActive = true, version })).Status.ShouldBe(200);
        (await setup.DeleteAsync(SetupUser.CompanyAdminC1, $"/api/v1/service-types/{cardsId}")).Status.ShouldBe(204);
        await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types");

        var rows = await setup.AuditRowsAsync();

        rows.Count.ShouldBe(3);
        rows.Select(r => (r.EntityName, r.Action)).ShouldBe([("service_type", "create"), ("priority_type", "update"), ("service_type", "delete")]);
        rows.Select(r => r.EntityId).ShouldBe([cardsId, SeedIds.Vip, cardsId]);
        foreach (var row in rows)
        {
            row.CompanyId.ShouldBe(SeedIds.Company1);
            row.UserId.ShouldBe(setup.Data.UserId(SetupUser.CompanyAdminC1));
            row.DeviceId.ShouldBeNull();
            row.At.ShouldBeInRange(before, DateTimeOffset.UtcNow.AddSeconds(1));
            row.At.Offset.ShouldBe(TimeSpan.Zero);
        }

        using var update = JsonDocument.Parse(rows[1].Data!);
        update.RootElement.GetProperty("feeAmount").GetDecimal().ShouldBe(60.00m);
        update.RootElement.GetProperty("version").GetString().ShouldBe(version);
        using var create = JsonDocument.Parse(rows[0].Data!);
        create.RootElement.GetProperty("prefix").GetString().ShouldBe("H");
        create.RootElement.GetProperty("nameAr").GetString().ShouldBe("البطاقات");
    }

    [Fact]
    public async Task Ac27_failed_writes_change_nothing_and_write_no_audit_row()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var v1 = (await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types/" + SeedIds.Service(3))).Json.Clone();
        var staleVersion = v1.GetProperty("version").GetString();
        (await setup.PutAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types/" + SeedIds.Service(3),
            new { nameEn = "Fresh", nameAr = "ج", prefix = "C", sortOrder = 3, isActive = true, version = staleVersion })).Status.ShouldBe(200);
        var auditBefore = await setup.AuditCountAsync();

        var taken = await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { nameEn = "x", nameAr = "ع", prefix = "A" });
        var stale = await setup.PutAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types/" + SeedIds.Service(3),
            new { nameEn = "Stale", nameAr = "ج", prefix = "C", sortOrder = 3, isActive = true, version = staleVersion });
        var invalid = await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { nameEn = "", nameAr = "ع", prefix = "H" });

        taken.Status.ShouldBe(409);
        stale.Status.ShouldBe(409);
        invalid.Status.ShouldBe(400);
        (await setup.AuditCountAsync()).ShouldBe(auditBefore);
        (await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types/" + SeedIds.Service(3))).Json.GetProperty("nameEn").GetString().ShouldBe("Fresh");
        (await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types")).Json.GetArrayLength().ShouldBe(6);
    }

    // ---- AC-33

    [Fact]
    public async Task Ac33_service_type_changes_that_devices_show_notify_once_after_the_commit()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var url = "/api/v1/service-types/" + SeedIds.Service(3);
        var current = (await setup.GetAsync(SetupUser.CompanyAdminC1, url)).Json.Clone();

        async Task<JsonElement> PutAsync(JsonElement from, string? nameEn = null, string? prefix = null, bool? isActive = null, int? sortOrder = null, int? target = null)
        {
            var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, url, new
            {
                nameEn = nameEn ?? from.GetProperty("nameEn").GetString(), nameAr = from.GetProperty("nameAr").GetString(),
                prefix = prefix ?? from.GetProperty("prefix").GetString(), targetMinutes = target ?? 5, sortOrder = sortOrder ?? from.GetProperty("sortOrder").GetInt32(),
                isActive = isActive ?? from.GetProperty("isActive").GetBoolean(), version = from.GetProperty("version").GetString(),
            });
            reply.Status.ShouldBe(200, reply.Body);
            return reply.Json.Clone();
        }

        current = await PutAsync(current, sortOrder: 30);
        current = await PutAsync(current, target: 15);
        setup.Notifier.Calls.ShouldBeEmpty();

        current = await PutAsync(current, nameEn: "Renamed");
        setup.Notifier.Calls.Count.ShouldBe(1);
        setup.Notifier.Calls[0].CompanyId.ShouldBe(SeedIds.Company1);
        current = await PutAsync(current, prefix: "K");
        setup.Notifier.Calls.Count.ShouldBe(2);
        current = await PutAsync(current, isActive: false);
        setup.Notifier.Calls.Count.ShouldBe(3);

        // A failed request (stale version) and create / delete announce nothing.
        (await setup.PutAsync(SetupUser.CompanyAdminC1, url, new { nameEn = "Late", nameAr = "ج", prefix = "K", sortOrder = 3, isActive = true, version = "AAAAAA" })).Status.ShouldBe(409);
        var created = await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { nameEn = "Cards", nameAr = "ب", prefix = "H" });
        (await setup.DeleteAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types/" + SetupHost.Id(created))).Status.ShouldBe(204);
        setup.Notifier.Calls.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Ac33_priority_type_name_prefix_active_paid_and_fee_notify_but_level_does_not()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var url = "/api/v1/priority-types/" + SeedIds.Vip;
        var current = (await setup.GetAsync(SetupUser.CompanyAdminC1, url)).Json.Clone();

        async Task<JsonElement> PutAsync(JsonElement from, string? nameEn = null, string? prefix = null, bool? isActive = null, int? level = null, bool? isPaid = null, decimal? fee = null)
        {
            var paid = isPaid ?? from.GetProperty("isPaid").GetBoolean();
            var reply = await setup.PutAsync(SetupUser.CompanyAdminC1, url, new
            {
                nameEn = nameEn ?? from.GetProperty("nameEn").GetString(), nameAr = from.GetProperty("nameAr").GetString(),
                prefix = prefix ?? from.GetProperty("prefix").GetString(), priorityLevel = level ?? from.GetProperty("priorityLevel").GetInt32(),
                isPaid = paid, feeAmount = paid ? fee ?? from.GetProperty("feeAmount").GetDecimal() : (decimal?)null,
                isActive = isActive ?? from.GetProperty("isActive").GetBoolean(), version = from.GetProperty("version").GetString(),
            });
            reply.Status.ShouldBe(200, reply.Body);
            return reply.Json.Clone();
        }

        current = await PutAsync(current, level: 3);
        setup.Notifier.Calls.ShouldBeEmpty();
        current = await PutAsync(current, nameEn: "Gold");
        current = await PutAsync(current, prefix: "W");
        current = await PutAsync(current, isActive: false);
        current = await PutAsync(current, fee: 61m);
        current = await PutAsync(current, isPaid: false);
        setup.Notifier.Calls.Count.ShouldBe(5);
    }

    // ---- AC-37

    [Fact]
    public async Task Ac37_error_responses_are_problem_json_with_a_code_and_never_show_internals()
    {
        using var setup = await SetupHost.StartAsync(postgres);
        var replies = new List<(ApiReply Reply, int Status, string Code)>
        {
            (await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { nameEn = "", prefix = "h" }), 400, "validation.failed"),
            (await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types", new { nameEn = "x", nameAr = "ع", prefix = "A" }), 409, "service_type.prefix_taken"),
            (await setup.GetAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types/" + Guid.NewGuid()), 404, "service_type.not_found"),
            (await setup.DeleteAsync(SetupUser.CompanyAdminC1, "/api/v1/service-types/" + Service1), 409, "service_type.in_use"),
            (await setup.GetAsync(SetupUser.CounterStaff, "/api/v1/service-types"), 403, "auth.forbidden"),
            (await setup.GetAsync(null, "/api/v1/service-types"), 401, "auth.unauthorized"),
            (await setup.PostAsync(SetupUser.CompanyAdminC1, "/api/v1/priority-types", new { nameEn = "x", nameAr = "ع", prefix = "P", priorityLevel = 0, isPaid = true }), 400, "validation.failed"),
            (await setup.PutAsync(SetupUser.CompanyAdminC1, "/api/v1/priority-types/" + SeedIds.Vip, new { nameEn = "x", nameAr = "ع", prefix = "V", priorityLevel = 1, isPaid = true, feeAmount = 1, isActive = true, version = "AAAAAA" }), 409, "request.conflict"),
        };

        foreach (var (reply, status, code) in replies)
        {
            reply.Status.ShouldBe(status, reply.Body);
            reply.Response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
            reply.Code.ShouldBe(code);
            reply.Json.GetProperty("status").GetInt32().ShouldBe(status);
            foreach (var forbidden in new[] { "Exception", "   at ", "SELECT", "INSERT", "ix_service_type", "fk_", "23505" })
            {
                reply.Body.ShouldNotContain(forbidden);
            }
        }

        var validation = replies[0].Reply.Json.GetProperty("errors");
        validation.EnumerateObject().Select(p => p.Name).ShouldBe(["nameEn", "nameAr", "prefix"], ignoreOrder: true);
        replies[6].Reply.Json.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["priorityLevel", "feeAmount"], ignoreOrder: true);
    }
}
