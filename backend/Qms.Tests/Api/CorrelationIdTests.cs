using Qms.Tests.Support;

namespace Qms.Tests.Api;

public sealed class CorrelationIdTests
{
    [Fact]
    public async Task Incoming_correlation_id_is_echoed_and_present_on_every_log_line_of_the_request()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/does-not-exist");
        request.Headers.Add("X-Correlation-Id", "abc-123");

        using var response = await client.SendAsync(request, CancellationToken.None);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldBe("abc-123");
        var requestLogs = await factory.LogSink.WaitForAsync(
            e => e.MessageTemplate.Text.Contains("HTTP", StringComparison.Ordinal));
        requestLogs.ShouldNotBeEmpty();
        requestLogs.ShouldAllBe(e => e.Properties.ContainsKey("CorrelationId") && e.Properties["CorrelationId"].ToString() == "\"abc-123\"");
    }

    [Fact]
    public async Task Exception_log_line_carries_the_correlation_id()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/boom");
        request.Headers.Add("X-Correlation-Id", "boom-1");

        using var response = await client.SendAsync(request, CancellationToken.None);

        var errorLog = (await factory.LogSink.WaitForAsync(e => e.Exception is not null)).Single();
        errorLog.Properties["CorrelationId"].ToString().ShouldBe("\"boom-1\"");
    }

    [Fact]
    public async Task Missing_correlation_id_gets_a_new_one_per_request()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();

        using var first = await client.GetAsync("/health/live", CancellationToken.None);
        using var second = await client.GetAsync("/health/live", CancellationToken.None);

        var firstId = first.Headers.GetValues("X-Correlation-Id").Single();
        var secondId = second.Headers.GetValues("X-Correlation-Id").Single();
        firstId.ShouldNotBeNullOrWhiteSpace();
        secondId.ShouldNotBe(firstId);
    }

    [Fact]
    public async Task Unsafe_correlation_id_is_replaced()
    {
        using var factory = new QmsApiFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "bad value with spaces");

        using var response = await client.SendAsync(request, CancellationToken.None);

        response.Headers.GetValues("X-Correlation-Id").Single().ShouldNotBe("bad value with spaces");
    }
}
