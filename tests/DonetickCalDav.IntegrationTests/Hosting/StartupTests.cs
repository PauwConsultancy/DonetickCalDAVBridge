using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace DonetickCalDav.IntegrationTests.Hosting;

/// <summary>IT-13: configuration validation at startup and the first load from Donetick.</summary>
[Trait("Scenario", "IT-13")]
[Trait("Covers", "UC-10")]
[Trait("Covers", "UC-12")]
[Trait("Covers", "FR-20")]
[Trait("Covers", "FR-23")]
public class StartupTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Theory(Timeout = DefaultTimeout)]
    [InlineData("Donetick:ApiKey", "", "ApiKey")]
    [InlineData("Donetick:PollIntervalSeconds", "2", "PollIntervalSeconds")]
    [InlineData("Donetick:PollIntervalSeconds", "3601", "PollIntervalSeconds")]
    [InlineData("CalDav:Username", "", "Username")]
    [InlineData("CalDav:Password", "", "Password")]
    [InlineData("CalDav:ListenPort", "0", "ListenPort")]
    public async Task Start_InvalidSetting_FailsAndNamesTheSetting(string key, string value, string expectedInMessage)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));

        var start = () => StartBridgeAsync(o => o.Settings[key] = value);

        var failure = await start.Should().ThrowAsync<BridgeStartException>();
        failure.Which.AllMessages.Should().Contain(expectedInMessage);
    }

    [Theory(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-27")]
    [InlineData("")]
    [InlineData("not a url")]
    public async Task Start_MalformedBaseUrl_FailsWithABareUriErrorThatDoesNotNameTheSetting(string baseUrl)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));

        var start = () => StartBridgeAsync(o => o.Settings["Donetick:BaseUrl"] = baseUrl);

        // The HttpClient set-up parses the URL before options validation runs, so the user sees
        // "Invalid URI" instead of the validation message for Donetick__BaseUrl.
        var failure = await start.Should().ThrowAsync<BridgeStartException>();
        var cause = failure.Which.Causes.OfType<UriFormatException>().Should().NotBeEmpty().And.Subject.First();
        cause.Message.Should().NotContain("BaseUrl");
        failure.Which.AllMessages.Should().NotContain("BaseUrl");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Start_DonetickDownForTheFirstAttempt_LoadsOnTheSecondStartupAttempt()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.Simple(2, "Second"))
            .FailNext("GET", DonetickStub.ChoresPath, times: 4, statusCode: 503);
        var bridge = await StartBridgeAsync(o => o.WaitForInitialSync = false);
        var ct = TestContext.Current.CancellationToken;

        await bridge.Time.AdvanceWhenScheduledAsync(BridgeHost.StartupDelay, ct: ct);
        await bridge.Time.AdvanceThroughAsync(BridgeHost.RetryDelays, ct);      // first attempt: 4 calls, then gives up
        await bridge.Time.AdvanceWhenScheduledAsync(TimeSpan.FromSeconds(3), ct: ct); // startup back-off
        await bridge.Time.WaitForTimerAsync(bridge.Options.PollInterval, ct: ct);

        bridge.Cache.GetAllChores().Should().HaveCount(2);
        Donetick.Count("GET", DonetickStub.ChoresPath).Should().Be(6, "4 failed calls, the second startup attempt and the first poll");
        bridge.Logs.Contains(LogLevel.Warning, "Startup sync attempt 1/3 failed").Should().BeTrue();
        bridge.Logs.Contains(LogLevel.Information, "Startup sync successful").Should().BeTrue();
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Start_BeforeTheFirstLoad_ServesAnEmptyCalendar()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync(o => o.WaitForInitialSync = false);

        var members = await bridge.CreateClient().PropFindAsync(bridge.CalendarPath(), 1, Dav.D + "getetag");

        members.Hrefs.Should().Equal(bridge.CalendarPath());
        Donetick.Count("GET", DonetickStub.ChoresPath).Should().Be(0, "the first load waits for the startup delay");
    }
}
