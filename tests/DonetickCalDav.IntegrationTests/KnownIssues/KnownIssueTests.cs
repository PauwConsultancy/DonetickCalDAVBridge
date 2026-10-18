using System.Net;
using System.Text;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.KnownIssues;

/// <summary>
/// IT-16: characterization tests for open findings (docs/findings.md). Each asserts today's behaviour;
/// when a finding is fixed, its test fails and is inverted into a regression test.
/// </summary>
[Trait("Scenario", "IT-16")]
public class KnownIssueTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-01")]
    public async Task Complete_ReloadFailsAfterASuccessfulCompletion_Returns502AndTheRetryCompletesAgain()
    {
        Donetick.WithChores(TestChoreFactory.DailyRecurring(3)).OnComplete(3);
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        Donetick.FailAlways("GET", DonetickStub.ChoresPath, statusCode: 503);
        var completed = Ics.VTodo(Ics.ChoreUid(3), "Daily chore", status: "COMPLETED");

        var first = client.PutAsync(bridge.ResourcePath(3), completed);
        await bridge.Time.AdvanceThroughAsync(BridgeHost.RetryDelays, TestContext.Current.CancellationToken);
        (await first).Status.Should().Be(HttpStatusCode.BadGateway, "the completion succeeded, but the reload did not");

        Donetick.RemoveFaults();
        (await client.PutAsync(bridge.ResourcePath(3), completed)).Status.Should().Be(HttpStatusCode.NoContent);

        Donetick.Count("POST", $"{DonetickStub.ChoresPath}/3/complete").Should().Be(2, "the client's retry completes the chore twice");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-02")]
    public async Task Create_DonetickFailsWith5xx_IsRetriedSoAnAppliedCreateWouldBeDuplicated()
    {
        Donetick.WithReferenceData().FailNext("POST", DonetickStub.ChoresPath, times: 1, statusCode: 502);
        var bridge = await StartBridgeAsync();
        const string uid = "0E984725-C51C-4BF4-9960-E1C80E27ABA0";

        var put = bridge.CreateClient().PutAsync(bridge.CalendarPath() + uid + ".ics", Ics.VTodo(uid, "Duplicate me"));
        await bridge.Time.AdvanceWhenScheduledAsync(BridgeHost.RetryDelays[0], ct: TestContext.Current.CancellationToken);

        (await put).Status.Should().Be(HttpStatusCode.Created);
        Donetick.Count("POST", DonetickStub.ChoresPath).Should().Be(2, "POST is retried like an idempotent request");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-02")]
    public async Task Complete_DonetickFailsWith5xx_IsRetriedSoAnAppliedCompletionWouldBeDoubled()
    {
        Donetick.WithChores(TestChoreFactory.DailyRecurring(3)).OnComplete(3)
            .FailNext("POST", $"{DonetickStub.ChoresPath}/3/complete", times: 1, statusCode: 500);
        var bridge = await StartBridgeAsync();

        var put = bridge.CreateClient().PutAsync(bridge.ResourcePath(3), Ics.VTodo(Ics.ChoreUid(3), "Daily chore", status: "COMPLETED"));
        await bridge.Time.AdvanceWhenScheduledAsync(BridgeHost.RetryDelays[0], ct: TestContext.Current.CancellationToken);

        (await put).Status.Should().Be(HttpStatusCode.NoContent);
        Donetick.Count("POST", $"{DonetickStub.ChoresPath}/3/complete").Should().Be(2);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-04")]
    public async Task Poll_DescriptionChangedWithoutNewUpdatedAt_KeepsServingTheOldVersion()
    {
        var chore = TestChoreFactory.Simple(1);
        Donetick.WithChores(chore);
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var before = await client.GetAsync(bridge.ResourcePath(1));

        var changed = TestChoreFactory.Simple(1);
        changed.Description = "Changed in Donetick";
        changed.Priority = 4;
        Donetick.WithChores(changed);
        await bridge.RunPollAsync();
        var after = await client.GetAsync(bridge.ResourcePath(1));

        // The ETag ignores description and priority, so the cache keeps the old entry as "unchanged".
        after.ETag.Should().Be(before.ETag);
        Ics.Value(after.Body, "DESCRIPTION").Should().Be("Test description");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-05")]
    public async Task AllDayEvents_DueShortlyAfterLocalMidnight_ShowsTheUtcDate()
    {
        // 22:30 UTC on 15 June is 00:30 on 16 June in Europe/Amsterdam.
        Donetick.WithChores(TestChoreFactory.WithDueDate(2, new DateTime(2025, 6, 15, 22, 30, 0, DateTimeKind.Utc)));
        var bridge = await StartBridgeAsync(o => o.AllDayEvents = true);

        var response = await bridge.CreateClient().GetAsync(bridge.ResourcePath(2));

        Ics.Line(response.Body, "DUE").Should().Be("DUE;VALUE=DATE:20250615");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-07")]
    public async Task Report_ChunkedBodyWithoutContentLength_IsTreatedAsEmpty()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().SendAsync(new HttpMethod("REPORT"), bridge.CalendarPath(), depth: 1,
            configure: r =>
            {
                r.Content = new StreamContent(new UnseekableStream(Encoding.UTF8.GetBytes(Dav.CalendarQuery())));
                r.Content.Headers.ContentType = new("application/xml");
            });

        response.Status.Should().Be(HttpStatusCode.BadRequest, "the body is only read when Content-Length is set");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-11")]
    public async Task PropFind_OnTheUuidPathOfACreatedChore_Returns404WhileGetWorks()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        const string uid = "7C9E6679-7425-40DE-944B-E07FC1F90AE7";
        var path = bridge.CalendarPath() + uid + ".ics";
        await client.PutAsync(path, Ics.VTodo(uid, "Created on the phone"));

        (await client.GetAsync(path)).Status.Should().Be(HttpStatusCode.OK);
        (await client.PropFindAsync(path, 0, Dav.D + "getetag")).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-12")]
    public async Task PropFind_OnTheBareHost_ReturnsTheHtmlStatusPage()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().PropFindAsync("/", 0);

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Header("Content-Type").Should().StartWith("text/html");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-16")]
    public async Task Startup_LoadsTheChoreListTwiceBackToBack()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));

        await StartBridgeAsync();

        Donetick.Count("GET", DonetickStub.ChoresPath).Should().Be(2, "startup check and first loop iteration");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-23")]
    public async Task PreserveScheduledTime_TimeFromDonetickIsAnRfc3339Timestamp_IsIgnored()
    {
        // Donetick 0.1.82 only accepts and returns frequencyMetadata.time as an RFC3339 timestamp; the
        // bridge parses it as a time of day, which fails silently, so DUE keeps the drifted time.
        var drifted = TestChoreFactory.DailyRecurring(3);
        drifted.NextDueDate = new DateTime(2025, 6, 16, 17, 45, 0, DateTimeKind.Utc);
        drifted.FrequencyMetadata = new() { Time = "2025-06-10T06:00:00Z", Timezone = "Europe/Amsterdam" };
        Donetick.WithChores(drifted);
        var bridge = await StartBridgeAsync(o => o.PreserveScheduledTime = true);

        var response = await bridge.CreateClient().GetAsync(bridge.ResourcePath(3));

        Ics.Line(response.Body, "DUE").Should().Be("DUE:20250616T174500Z");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-30")]
    public async Task PreserveScheduledTime_ScheduledTimeInTheSpringForwardGap_BreaksEveryRequestForTheChore()
    {
        // Only reachable with HH:mm metadata today (finding F-23 blocks RFC3339 values); fixing F-23
        // without F-30 would expose it for every chore scheduled in the skipped hour.
        var chore = TestChoreFactory.DailyRecurring(3);
        chore.NextDueDate = new DateTime(2025, 3, 30, 17, 45, 0, DateTimeKind.Utc);
        chore.FrequencyMetadata = new() { Time = "02:30", Timezone = "Europe/Amsterdam" };
        Donetick.WithChores(chore, TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync(o => o.PreserveScheduledTime = true);
        var client = bridge.CreateClient();

        var get = () => client.GetAsync(bridge.ResourcePath(3));
        var report = () => client.ReportAsync(bridge.CalendarPath(), Dav.CalendarQuery());

        // TestServer surfaces the exception; Kestrel answers 500. The REPORT fails for the whole list.
        await get.Should().ThrowAsync<ArgumentException>();
        await report.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class UnseekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
