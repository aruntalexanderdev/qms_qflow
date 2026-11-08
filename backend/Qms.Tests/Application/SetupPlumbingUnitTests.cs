using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Qms.Application.Auth;
using Qms.Application.Common;
using Qms.Application.PriorityTypes;
using Qms.Application.ServiceTypes;
using Qms.Infrastructure.Errors;
using Qms.Infrastructure.Notifications;
using Qms.Tests.Support;

namespace Qms.Tests.Application;

// STORY-005 unit tests that need no database: version helpers (AC-32), validators (AC-5, AC-17), permission map,
// error catalog defaults (AC-29) and the production notifier registration (AC-33).
public sealed class SetupPlumbingUnitTests
{
    // ---- AC-32

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(1234u)]
    [InlineData(uint.MaxValue)]
    public void Ac32_a_row_version_converts_to_an_opaque_string_and_back(uint xmin)
    {
        var version = RowVersionCodec.Encode(xmin);

        version.ShouldNotBeNullOrEmpty();
        RowVersionCodec.TryDecode(version, out var decoded).ShouldBeTrue();
        decoded.ShouldBe(xmin);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1234")]
    [InlineData("not-a-version")]
    [InlineData("AAAAAAAA")]
    [InlineData("AAAAB!")]
    [InlineData("AAAAAB")]
    public void Ac32_an_unreadable_version_is_rejected_not_thrown(string? text) =>
        RowVersionCodec.TryDecode(text, out _).ShouldBeFalse();

    [Fact]
    public async Task Ac32_a_bad_version_is_a_validation_error_on_version()
    {
        var validator = new UpdateServiceTypeValidator(new FakeCompanyLanguages());
        var command = new UpdateServiceTypeCommand(Guid.NewGuid(), "x", null, "A", null, 0, true, "garbage");

        var result = await validator.ValidateAsync(command);

        result.Errors.Select(e => e.PropertyName).ShouldBe(["Version"]);
    }

    [Fact]
    public void Ac32_the_set_version_ignores_order_and_changes_with_any_row_or_xmin()
    {
        var (a, b, c) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var forward = RowVersionCodec.ForSet([(a, 1u), (b, 2u), (c, 3u)]);
        var shuffled = RowVersionCodec.ForSet([(c, 3u), (a, 1u), (b, 2u)]);

        shuffled.ShouldBe(forward);
        forward.ShouldNotBeNullOrEmpty();
        RowVersionCodec.ForSet([(a, 1u), (b, 2u), (c, 4u)]).ShouldNotBe(forward);
        RowVersionCodec.ForSet([(a, 1u), (b, 2u), (Guid.NewGuid(), 3u)]).ShouldNotBe(forward);
        RowVersionCodec.ForSet([(a, 1u), (b, 2u)]).ShouldNotBe(forward);
        RowVersionCodec.ForSet([]).ShouldNotBeNullOrEmpty();
    }

    // ---- AC-5

    private static string[] ServiceFields(string? nameEn = "Cards", IReadOnlyDictionary<string, string?>? translations = null, string? prefix = "H", int? target = null, int sort = 0) =>
        [.. new CreateServiceTypeValidator(new FakeCompanyLanguages(FakeCompanyLanguages.EnglishAndArabic))
            .ValidateAsync(new CreateServiceTypeCommand(nameEn, translations, prefix, target, sort, true)).GetAwaiter().GetResult().Errors.Select(e => e.PropertyName)];

    [Fact]
    public void Ac5_service_type_rules()
    {
        ServiceFields().ShouldBeEmpty();
        ServiceFields(prefix: " H ").ShouldBeEmpty();
        ServiceFields(nameEn: new string('x', 200), target: 1).ShouldBeEmpty();
        ServiceFields(nameEn: "").ShouldBe(["Name"]);
        ServiceFields(nameEn: "  ").ShouldBe(["Name"]);
        ServiceFields(nameEn: new string('x', 201)).ShouldBe(["Name"]);
        ServiceFields(nameEn: null).ShouldBe(["Name"]);
        ServiceFields(translations: new Dictionary<string, string?> { ["ar"] = "ب" }).ShouldBeEmpty();
        ServiceFields(translations: new Dictionary<string, string?> { ["ar"] = null }).ShouldBeEmpty();
        ServiceFields(translations: new Dictionary<string, string?> { ["ar"] = new string('ع', 201) }).ShouldBe(["Translations.ar"]);
        ServiceFields(translations: new Dictionary<string, string?> { ["fr"] = "x" }).ShouldBe(["Translations.fr"]);
        ServiceFields(translations: new Dictionary<string, string?> { ["en"] = "x" }).ShouldBe(["Translations.en"]);
        ServiceFields(translations: new Dictionary<string, string?> { ["ar"] = "x", ["AR"] = "y" }).ShouldBe(["Translations.ar"]);
        foreach (var prefix in new[] { "h", "HB", "1", "", "  ", null, "É", "-" })
        {
            ServiceFields(prefix: prefix).ShouldBe(["Prefix"], prefix ?? "null");
        }

        ServiceFields(target: 0).ShouldBe(["TargetMinutes"]);
        ServiceFields(target: -5).ShouldBe(["TargetMinutes"]);
        ServiceFields(sort: -1).ShouldBe(["SortOrder"]);
    }

    // ---- AC-17

    private static string[] PriorityFields(string? prefix = "P", int? level = 1, bool? paid = false, decimal? fee = null, string? nameEn = "Premium", IReadOnlyDictionary<string, string?>? translations = null) =>
        [.. new CreatePriorityTypeValidator(new FakeCompanyLanguages(FakeCompanyLanguages.EnglishAndArabic))
            .ValidateAsync(new CreatePriorityTypeCommand(nameEn, translations ?? new Dictionary<string, string?> { ["ar"] = "مميز" }, prefix, level, paid, fee, true)).GetAwaiter().GetResult().Errors.Select(e => e.PropertyName)];

    [Fact]
    public void Ac17_priority_type_rules()
    {
        PriorityFields().ShouldBeEmpty();
        PriorityFields(paid: true, fee: 75.50m).ShouldBeEmpty();
        PriorityFields(paid: true, fee: 0m).ShouldBeEmpty();
        PriorityFields(paid: true, fee: 9_999_999_999.99m).ShouldBeEmpty();
        PriorityFields(nameEn: "").ShouldBe(["Name"]);
        foreach (var prefix in new[] { "p", "PV", "7", "" })
        {
            PriorityFields(prefix: prefix).ShouldBe(["Prefix"], prefix);
        }

        PriorityFields(nameEn: null).ShouldBe(["Name"]);
        PriorityFields(translations: new Dictionary<string, string?> { ["en"] = "x" }).ShouldBe(["Translations.en"]);
        PriorityFields(translations: new Dictionary<string, string?>()).ShouldBeEmpty();
        PriorityFields(level: null).ShouldBe(["PriorityLevel"]);
        PriorityFields(paid: null).ShouldBe(["IsPaid"]);
        PriorityFields(level: 0).ShouldBe(["PriorityLevel"]);
        PriorityFields(level: -1).ShouldBe(["PriorityLevel"]);
        PriorityFields(paid: true).ShouldBe(["FeeAmount"]);
        PriorityFields(paid: true, fee: 12.345m).ShouldBe(["FeeAmount"]);
        PriorityFields(paid: true, fee: -1m).ShouldBe(["FeeAmount"]);
        PriorityFields(paid: false, fee: 10m).ShouldBe(["FeeAmount"]);
        PriorityFields(paid: false, fee: 0m).ShouldBe(["FeeAmount"]);
        PriorityFields(paid: true, fee: 10_000_000_000m).ShouldBe(["FeeAmount"]);
    }

    // ---- permissions (product owner change: permission-based authorization)

    [Fact]
    public void The_static_map_reproduces_the_adr_008_matrix()
    {
        RolePermissionMap.For(Roles.CompanyAdmin).ShouldBe(Permissions.All, ignoreOrder: true);
        RolePermissionMap.For(Roles.BranchManager).ShouldBe(
            [Permissions.ServiceTypes.View, Permissions.PriorityTypes.View], ignoreOrder: true);
        RolePermissionMap.For(Roles.CounterStaff).ShouldBeEmpty();
        RolePermissionMap.For(Roles.SuperAdmin).ShouldBeEmpty();
        RolePermissionMap.For(null).ShouldBeEmpty();
        RolePermissionMap.For("Unknown").ShouldBeEmpty();
        Permissions.All.ShouldAllBe(p => p.Contains('.') && p == p.ToLowerInvariant());
        Permissions.All.Distinct().Count().ShouldBe(Permissions.All.Count);
    }

    [Fact]
    public async Task The_resolver_needs_a_signed_in_user_with_a_company()
    {
        var resolver = new Qms.Infrastructure.Authorization.StaticRolePermissionResolver();
        var company = Guid.NewGuid();

        (await resolver.HasPermissionAsync(new FakeUser(company, Roles.CompanyAdmin), Permissions.ServiceTypes.Delete, default)).ShouldBeTrue();
        (await resolver.HasPermissionAsync(new FakeUser(company, Roles.BranchManager), Permissions.ServiceTypes.View, default)).ShouldBeTrue();
        (await resolver.HasPermissionAsync(new FakeUser(company, Roles.BranchManager), Permissions.ServiceTypes.Edit, default)).ShouldBeFalse();
        (await resolver.HasPermissionAsync(new FakeUser(null, Roles.CompanyAdmin), Permissions.ServiceTypes.View, default)).ShouldBeFalse();
        (await resolver.HasPermissionAsync(new FakeUser(company, Roles.CompanyAdmin, authenticated: false), Permissions.ServiceTypes.View, default)).ShouldBeFalse();
    }

    // ---- AC-29 defaults, AC-33 production registration

    [Fact]
    public void Ac29_the_catalog_has_defaults_per_sqlstate_and_ignores_other_states()
    {
        PostgresErrorCatalog.Map("23505", null)!.Code.ShouldBe("resource.duplicate");
        PostgresErrorCatalog.Map("23503", "nope")!.Code.ShouldBe("resource.in_use");
        PostgresErrorCatalog.Map("23001", null)!.Code.ShouldBe("resource.in_use");
        PostgresErrorCatalog.Map("23514", "nope")!.Type.ShouldBe(ErrorType.Validation);
        PostgresErrorCatalog.Map("40001", null).ShouldBeNull();
        PostgresErrorCatalog.Map(null, null).ShouldBeNull();
    }

    [Fact]
    public async Task Ac33_the_production_notifier_is_a_no_op_that_never_throws()
    {
        var services = new ServiceCollection();
        Qms.Infrastructure.DependencyInjection.AddInfrastructure(services, new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Qms"] = QmsApiFactory.UnusedConnectionString }).Build());
        using var provider = services.BuildServiceProvider();

        var notifier = provider.GetRequiredService<IDeviceConfigNotifier>();

        notifier.ShouldBeOfType<NoOpDeviceConfigNotifier>();
        await notifier.ConfigChangedAsync(new DeviceConfigChange(Guid.NewGuid()), CancellationToken.None);
    }
}

internal sealed class FakeUser(Guid? companyId, string role, bool authenticated = true, Guid? id = null) : ICurrentUser
{
    public bool IsAuthenticated => authenticated;

    public Guid? UserId { get; } = id ?? Guid.NewGuid();

    public Guid? CompanyId => companyId;

    public string? Role => role;

    public string? Language => "en";

    public bool IsInRole(string value) => value == role;
}

public sealed class ErrorCodeSelectionTests
{
    [Fact]
    public async Task A_shared_specific_error_code_becomes_the_validation_code()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<Qms.Application.Localisation.ICompanyLanguages>(new FakeCompanyLanguages());
        var provider = services.AddHandlersAndValidators(typeof(Qms.Application.Common.Result).Assembly).BuildServiceProvider();
        var validator = provider.GetRequiredService<FluentValidation.IValidator<CreateServiceTypeCommand>>();

        var result = await validator.ValidateAsync(new CreateServiceTypeCommand("x", new Dictionary<string, string?> { ["fr"] = "x" }, "H", null, 0, true));

        result.Errors.Single().ErrorCode.ShouldBe("translation.language_not_enabled");
    }
}
