using System.Net;
using Qms.Tests.Support;

namespace Qms.Tests.Api;

public sealed class HostingTests
{
    [Fact]
    public async Task Swagger_ui_and_openapi_document_are_served_in_development()
    {
        using var factory = new QmsApiFactory("Development");
        using var client = factory.CreateClient();

        var ui = await client.GetAsync("/swagger/index.html", CancellationToken.None);
        var document = await client.GetAsync("/openapi/v1.json", CancellationToken.None);

        ui.StatusCode.ShouldBe(HttpStatusCode.OK);
        document.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = await document.Content.ReadAsStringAsync(CancellationToken.None);
        json.ShouldContain("\"version\": \"v1\"");
        json.ShouldContain("/api/v1/test/things");
    }

    [Fact]
    public async Task Swagger_ui_and_openapi_document_are_not_served_in_production()
    {
        using var factory = new QmsApiFactory("Production");
        using var client = factory.CreateClient();

        var ui = await client.GetAsync("/swagger/index.html", CancellationToken.None);
        var document = await client.GetAsync("/openapi/v1.json", CancellationToken.None);

        ui.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        document.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Hsts_header_is_sent_over_https_outside_development()
    {
        using var factory = new QmsApiFactory("Production");
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://qms.example.test") });

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.Contains("Strict-Transport-Security").ShouldBeTrue();
    }

    [Fact]
    public async Task Hsts_header_is_not_sent_in_development()
    {
        using var factory = new QmsApiFactory("Development");
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://qms.example.test") });

        using var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }
}
