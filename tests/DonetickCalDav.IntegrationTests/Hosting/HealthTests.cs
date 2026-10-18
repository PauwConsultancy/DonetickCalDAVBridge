using System.Net;
using System.Text.Json;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Hosting;

/// <summary>IT-14: the health endpoint and the HTML status page.</summary>
[Trait("Scenario", "IT-14")]
[Trait("Covers", "UC-11")]
public class HealthTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    public async Task HealthJson_WithChores_Returns200WithCacheFigures()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.WithDueDate(2), TestChoreFactory.Inactive(5));
        var bridge = await StartBridgeAsync(o => o.PollIntervalSeconds = 45);

        var response = await bridge.CreateClient().Anonymous().GetAsync("/health/json");

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Header("Content-Type").Should().Be("application/json; charset=utf-8");
        var json = JsonDocument.Parse(response.Body).RootElement;
        json.GetProperty("status").GetString().Should().Be("healthy");
        json.GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        json.GetProperty("cache").GetProperty("total_chores").GetInt32().Should().Be(3);
        json.GetProperty("cache").GetProperty("active").GetInt32().Should().Be(2);
        json.GetProperty("cache").GetProperty("with_due_date").GetInt32().Should().Be(1);
        json.GetProperty("donetick").GetProperty("poll_interval_seconds").GetInt32().Should().Be(45);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-13")]
    public async Task HealthJson_AccountWithoutChores_ReportsUnhealthy()
    {
        Donetick.WithChores();
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().Anonymous().GetAsync("/health/json");

        response.Status.Should().Be(HttpStatusCode.ServiceUnavailable, "an empty account is indistinguishable from a failed load");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-13")]
    public async Task HealthJson_DonetickDownSinceTheLastPoll_StillReportsHealthy()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        Donetick.FailAlways("GET", DonetickStub.ChoresPath, statusCode: 503);
        var ct = TestContext.Current.CancellationToken;
        await bridge.Time.AdvanceWhenScheduledAsync(bridge.Options.PollInterval, ct: ct);
        await bridge.Time.AdvanceThroughAsync(BridgeHost.RetryDelays, ct);
        await bridge.Time.WaitForTimerAsync(bridge.Options.PollInterval, ct: ct);

        var response = await bridge.CreateClient().Anonymous().GetAsync("/health/json");

        response.Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-06")]
    public async Task StatusEndpoints_Anonymous_ExposeDonetickUrlAndUserName()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var anonymous = bridge.CreateClient().Anonymous();

        var json = await anonymous.GetAsync("/health/json");
        var html = await anonymous.GetAsync("/");

        JsonDocument.Parse(json.Body).RootElement.GetProperty("donetick").GetProperty("base_url").GetString()
            .Should().Be(Donetick.Url);
        html.Body.Should().Contain(BridgeHost.Username);
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("/")]
    [InlineData("/health")]
    public async Task StatusPage_Renders_WithConfigurationValuesHtmlEncoded(string path)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync(o => o.CalendarName = "<script>alert(1)</script>");

        var response = await bridge.CreateClient().Anonymous().GetAsync(path);

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Header("Content-Type").Should().StartWith("text/html");
        response.Body.Should().Contain("Donetick CalDAV Bridge");
        response.Body.Should().NotContain("<script>alert(1)</script>").And.Contain("&lt;script&gt;");
    }
}
