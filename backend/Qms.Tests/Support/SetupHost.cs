using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Qms.Domain.Entities;
using Qms.Tests.Persistence;

namespace Qms.Tests.Support;

/// <summary>
/// A real API host on a fresh seeded database plus <see cref="SetupTestData"/> (ADR-008 section 15). Every M2 setup test
/// starts one: call endpoints as a <see cref="SetupUser"/> (or anonymously), then look at the database and the audit log.
/// </summary>
public sealed class SetupHost : IDisposable
{
    private SetupHost(AuthHost host, SetupTestData data)
    {
        Host = host;
        Data = data;
    }

    public AuthHost Host { get; }

    public SetupTestData Data { get; }

    public RecordingDeviceConfigNotifier Notifier => Host.Factory.Services.GetRequiredService<RecordingDeviceConfigNotifier>();

    public SqlCommandLog Sql => Host.Factory.Services.GetRequiredService<SqlCommandLog>();

    public static async Task<SetupHost> StartAsync(PostgresFixture postgres)
    {
        // A clone of the template with the setup data already in it; EnsureAsync then only finds what exists.
        var host = AuthHost.StartOn(await postgres.NewSetupDatabaseAsync());
        return new SetupHost(host, await SetupTestData.EnsureAsync(host));
    }

    /// <summary>
    /// The ar-enabled test database (STORY-051): <c>ar</c> is made available and enabled for Company 1 only, by SQL in the test.
    /// The shipped seed never has this state (phase 1 is English only).
    /// </summary>
    public static async Task<SetupHost> StartWithArabicAsync(PostgresFixture postgres)
    {
        var setup = await StartAsync(postgres);
        await setup.MakeArabicAvailableAsync();
        await setup.SetEnabledLanguagesAsync(Qms.Infrastructure.Persistence.Seed.SeedIds.Company1, "en", "ar");
        return setup;
    }

    public void Dispose() => Host.Dispose();

    public Task MakeArabicAvailableAsync() => ExecuteAsync("UPDATE language SET is_available = true WHERE code = 'ar'");

    public Task MakeArabicUnavailableAsync() => ExecuteAsync("UPDATE language SET is_available = false WHERE code = 'ar'");

    /// <summary>Sets the enabled languages of a company by SQL (the default stays <c>en</c>).</summary>
    public Task SetEnabledLanguagesAsync(Guid companyId, params string[] codes) =>
        ExecuteAsync($"UPDATE company SET enabled_languages = {TextArray(codes)} WHERE id = '{companyId}'");

    /// <summary>Sets the languages a seeded dispenser offers by SQL.</summary>
    public Task SetDispenserLanguagesAsync(Guid deviceId, params string[] codes) =>
        ExecuteAsync($"UPDATE dispenser_setting SET languages = {TextArray(codes)} WHERE device_id = '{deviceId}'");

    private static string TextArray(string[] codes) => "ARRAY[" + string.Join(",", codes.Select(code => $"'{code}'")) + "]::text[]";

    private async Task ExecuteAsync(string sql)
    {
        await using var db = Host.OpenContext();
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>Calls the API as <paramref name="user"/>; a null user sends no token.</summary>
    public Task<ApiReply> CallAsync(SetupUser? user, HttpMethod method, string url, object? body = null) =>
        Host.SendAsync(method, url, body, user is { } signedIn ? Data.Token(signedIn) : null);

    public Task<ApiReply> GetAsync(SetupUser? user, string url) => CallAsync(user, HttpMethod.Get, url);

    /// <summary>A <c>GET</c> that sends an <c>Accept-Language</c> header (null sends none).</summary>
    public Task<ApiReply> GetWithLanguageAsync(SetupUser? user, string url, string? acceptLanguage) =>
        Host.SendAsync(HttpMethod.Get, url, null, user is { } signedIn ? Data.Token(signedIn) : null, acceptLanguage: acceptLanguage);

    public Task<ApiReply> PostAsync(SetupUser? user, string url, object body) => CallAsync(user, HttpMethod.Post, url, body);

    public Task<ApiReply> PutAsync(SetupUser? user, string url, object body) => CallAsync(user, HttpMethod.Put, url, body);

    public Task<ApiReply> DeleteAsync(SetupUser? user, string url) => CallAsync(user, HttpMethod.Delete, url);

    /// <summary>Every audit row of every company, oldest first.</summary>
    public async Task<List<AuditLog>> AuditRowsAsync()
    {
        await using var db = Host.OpenContext();
        return await db.AuditLogs.IgnoreQueryFilters().AsNoTracking().OrderBy(a => a.At).ThenBy(a => a.Id).ToListAsync();
    }

    public async Task<int> AuditCountAsync()
    {
        await using var db = Host.OpenContext();
        return await db.AuditLogs.IgnoreQueryFilters().CountAsync();
    }

    public static string Str(ApiReply reply, string property) => reply.Json.GetProperty(property).GetString()!;

    public static Guid Id(ApiReply reply) => reply.Json.GetProperty("id").GetGuid();

    public static JsonElement Item(ApiReply list, string prefix) =>
        list.Json.EnumerateArray().Single(item => item.GetProperty("prefix").GetString() == prefix);
}
