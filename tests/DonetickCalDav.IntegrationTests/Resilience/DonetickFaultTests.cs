using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace DonetickCalDav.IntegrationTests.Resilience;

/// <summary>
/// IT-12: Donetick faults. The retry pipeline waits 5, 15 and 40 seconds between attempts; fake time
/// skips those waits, and waiting for each delay to be scheduled proves the schedule itself.
/// </summary>
[Trait("Scenario", "IT-12")]
[Trait("Covers", "NFR-04")]
[Trait("Covers", "NFR-05")]
[Trait("Covers", "UC-12")]
public class DonetickFaultTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    private static readonly string Chore7 = $"{DonetickStub.ChoresPath}/7";

    [Fact(Timeout = DefaultTimeout)]
    public async Task Edit_DonetickFailsOnceWith5xx_IsRetriedAfterFiveSecondsAndSucceeds()
    {
        Donetick.WithChores(TestChoreFactory.Simple(7)).OnUpdate(TestChoreFactory.Simple(7, "Renamed"))
            .FailNext("PUT", Chore7, times: 1, statusCode: 503);
        var bridge = await StartBridgeAsync();

        var put = bridge.CreateClient().PutAsync(bridge.ResourcePath(7), Ics.VTodo(Ics.ChoreUid(7), "Renamed"));
        await bridge.Time.AdvanceWhenScheduledAsync(BridgeHost.RetryDelays[0], ct: TestContext.Current.CancellationToken);
        var response = await put;

        response.Status.Should().Be(HttpStatusCode.NoContent);
        Donetick.Count("PUT", Chore7).Should().Be(2);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Edit_DonetickKeepsFailing_Returns502AfterThreeRetries()
    {
        Donetick.WithChores(TestChoreFactory.Simple(7)).FailAlways("PUT", Chore7, statusCode: 500);
        var bridge = await StartBridgeAsync();

        var put = bridge.CreateClient().PutAsync(bridge.ResourcePath(7), Ics.VTodo(Ics.ChoreUid(7), "Renamed"));
        await bridge.Time.AdvanceThroughAsync(BridgeHost.RetryDelays, TestContext.Current.CancellationToken);
        var response = await put;

        response.Status.Should().Be(HttpStatusCode.BadGateway);
        Donetick.Count("PUT", Chore7).Should().Be(4, "one attempt plus three retries");
        bridge.Logs.Contains(LogLevel.Error, "Donetick API error").Should().BeTrue();
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task Edit_DonetickAnswers4xx_Returns502WithoutRetrying(int statusCode)
    {
        Donetick.WithChores(TestChoreFactory.Simple(7)).FailAlways("PUT", Chore7, statusCode);
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().PutAsync(bridge.ResourcePath(7), Ics.VTodo(Ics.ChoreUid(7), "Renamed"));

        response.Status.Should().Be(HttpStatusCode.BadGateway);
        Donetick.Count("PUT", Chore7).Should().Be(1);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Poll_DonetickDown_KeepsThePreviousCacheAndRecoversOnALaterPoll()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.Simple(2, "Second"));
        var bridge = await StartBridgeAsync();
        Donetick.FailAlways("GET", DonetickStub.ChoresPath, statusCode: 502);
        var ct = TestContext.Current.CancellationToken;

        await bridge.Time.AdvanceWhenScheduledAsync(bridge.Options.PollInterval, ct: ct);
        await bridge.Time.AdvanceThroughAsync(BridgeHost.RetryDelays, ct);
        await bridge.Time.WaitForTimerAsync(bridge.Options.PollInterval, ct: ct);

        bridge.Cache.GetAllChores().Should().HaveCount(2, "a failed poll keeps what was loaded before");
        (await bridge.CreateClient().GetAsync(bridge.ResourcePath(1))).Status.Should().Be(HttpStatusCode.OK);
        bridge.Logs.Contains(LogLevel.Warning, "unreachable during sync").Should().BeTrue();

        Donetick.RemoveFaults().WithChores(TestChoreFactory.Simple(2, "Second"));
        await bridge.RunPollAsync();
        bridge.Cache.GetAllChores().Should().ContainSingle(c => c.Chore.Id == 2);
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("not json at all")]
    [InlineData("\"a string\"")]
    [InlineData("[{\"id\":\"not a number\"}]")]
    public async Task Poll_InvalidJson_KeepsThePreviousCache(string body)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        Donetick.WithChoreListJson(body);
        await bridge.RunPollAsync();

        bridge.Cache.GetAllChores().Should().ContainSingle();
        bridge.Logs.Contains(LogLevel.Error, "Unexpected error during chore sync").Should().BeTrue();
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Poll_CapitalisedResWrapper_IsAccepted()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        Donetick.WithChoreListJson("""{"Res":[{"id":1,"name":"Wrapped","isActive":true},{"id":2,"name":"Also wrapped","isActive":true}]}""");
        await bridge.RunPollAsync();

        bridge.Cache.GetAllChores().Select(c => c.Chore.Name).Should().BeEquivalentTo("Wrapped", "Also wrapped");
    }

    [Theory(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-26")]
    [InlineData("""{"error":"Invalid API token"}""")]
    [InlineData("""{"data":[{"id":1,"name":"Renamed wrapper"}]}""")]
    [InlineData("""{"res":null}""")]
    [InlineData("""{"res":[{"id":1,"name":"Lower-case wrapper as Donetick's internal API writes it"}]}""")]
    public async Task Poll_JsonObjectWithoutCapitalisedRes_IsReadAsAnEmptyListAndEmptiesTheCache(string body)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.Simple(2, "Second"));
        var bridge = await StartBridgeAsync();

        Donetick.WithChoreListJson(body);
        await bridge.RunPollAsync();

        // The design (technical design, chapter 17) is to throw on an unknown shape so the cache
        // survives; in fact every JSON object without a capitalised "Res" counts as "no chores".
        bridge.Cache.GetAllChores().Should().BeEmpty();
    }
}
