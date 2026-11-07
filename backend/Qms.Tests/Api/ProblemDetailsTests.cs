using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Qms.Tests.Support;

namespace Qms.Tests.Api;

public sealed class ProblemDetailsTests : IDisposable
{
    private readonly QmsApiFactory _factory = new();
    private readonly HttpClient _client;

    public ProblemDetailsTests()
    {
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Unknown_route_returns_404_problem_details()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await ReadAsync(response);
        body.GetProperty("status").GetInt32().ShouldBe(404);
        body.TryGetProperty("title", out _).ShouldBeTrue();
        body.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
        body.GetProperty("correlationId").GetString().ShouldBe(response.Headers.GetValues("X-Correlation-Id").Single());
        body.GetProperty("code").GetString().ShouldBe("resource.not_found");
    }

    [Fact]
    public async Task Unhandled_exception_returns_500_problem_details_without_leaking_details()
    {
        var response = await _client.GetAsync("/api/v1/test/boom", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var raw = await response.Content.ReadAsStringAsync(CancellationToken.None);
        raw.ShouldNotContain("secret internal detail");
        raw.ShouldNotContain("InvalidOperationException");
        raw.ShouldNotContain(" at Qms.");
        var body = JsonDocument.Parse(raw).RootElement;
        body.GetProperty("status").GetInt32().ShouldBe(500);
        body.GetProperty("code").GetString().ShouldBe("server.unexpected_error");
        body.GetProperty("traceId").GetString().ShouldNotBeNullOrEmpty();
        body.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Invalid_request_returns_400_with_field_errors_and_code()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/test/things", new { name = "", quantity = 0 }, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        var body = await ReadAsync(response);
        body.GetProperty("code").GetString().ShouldBe("validation.failed");
        var errors = body.GetProperty("errors");
        errors.TryGetProperty("Name", out _).ShouldBeTrue();
        errors.TryGetProperty("Quantity", out _).ShouldBeTrue();
        body.GetProperty("correlationId").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Conflict_error_maps_to_409_with_its_code()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/test/things", new { name = "duplicate", quantity = 1 }, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("thing.duplicate");
    }

    [Fact]
    public async Task Valid_request_returns_200_with_value()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/test/things", new { name = "ok", quantity = 2 }, CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        Guid.TryParse((await response.Content.ReadAsStringAsync(CancellationToken.None)).Trim('"'), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Not_found_error_from_query_maps_to_404_with_its_code()
    {
        var response = await _client.GetAsync("/api/v1/test/things/42", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync(response)).GetProperty("code").GetString().ShouldBe("thing.not_found");
    }

    private static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(CancellationToken.None));
}
