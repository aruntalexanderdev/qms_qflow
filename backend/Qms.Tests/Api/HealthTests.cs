using System.Net;
using Qms.Tests.Support;

namespace Qms.Tests.Api;

public sealed class HealthTests
{
    [Fact]
    public async Task Live_returns_200_healthy()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_returns_200_when_all_checks_pass()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready", CancellationToken.None);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(CancellationToken.None)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Ready_returns_503_when_a_dependency_check_fails_but_live_stays_200()
    {
        using var factory = new QmsApiFactory(failingReadyCheck: true);
        using var client = factory.CreateClient();

        var ready = await client.GetAsync("/health/ready", CancellationToken.None);
        var live = await client.GetAsync("/health/live", CancellationToken.None);

        ready.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        live.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
