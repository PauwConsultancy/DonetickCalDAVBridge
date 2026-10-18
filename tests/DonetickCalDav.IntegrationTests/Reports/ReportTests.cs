using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Reports;

/// <summary>IT-04: the REPORT types Apple uses (calendar-query, calendar-multiget, sync-collection).</summary>
[Trait("Scenario", "IT-04")]
[Trait("Covers", "UC-02")]
[Trait("Covers", "FR-09")]
public class ReportTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    public async Task CalendarQuery_DefaultCalendar_ReturnsEveryChoreWithDataAndETag()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1, "Water the plants"), TestChoreFactory.WithDueDate(2));
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();

        var report = await client.ReportAsync(bridge.CalendarPath(), Dav.CalendarQuery());

        report.Status.Should().Be((HttpStatusCode)207);
        report.Hrefs.Should().BeEquivalentTo(bridge.ResourcePath(1), bridge.ResourcePath(2));
        var ics = report.CalendarData(bridge.ResourcePath(1))!;
        Ics.Value(ics, "UID").Should().Be(Ics.ChoreUid(1));
        Ics.Value(ics, "SUMMARY").Should().Be("Water the plants");

        var get = await client.GetAsync(bridge.ResourcePath(1));
        report.GetETag(bridge.ResourcePath(1)).Should().Be(get.ETag, "REPORT and GET must agree on the ETag");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task CalendarQuery_LabelCalendar_ReturnsOnlyThatListsChores()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = true);

        var report = await bridge.CreateClient().ReportAsync(bridge.CalendarPath("home"), Dav.CalendarQuery(Dav.D + "getetag"));

        report.Hrefs.Should().BeEquivalentTo(
            bridge.ResourcePath(ReferenceData.BuyGroceries, "home"),
            bridge.ResourcePath(ReferenceData.TakeOutTheBins, "home"));
        report.CalendarData(bridge.ResourcePath(ReferenceData.BuyGroceries, "home")).Should().BeNull("only getetag was asked for");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Multiget_RequestedHrefs_ReturnsExactlyThoseAndSkipsTheCollectionHref()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.Simple(2, "Second"), TestChoreFactory.Simple(3, "Third"));
        var bridge = await StartBridgeAsync();

        var report = await bridge.CreateClient().ReportAsync(bridge.CalendarPath(),
            Dav.CalendarMultiget([bridge.CalendarPath(), bridge.ResourcePath(1), bridge.ResourcePath(3)]));

        report.Status.Should().Be((HttpStatusCode)207);
        report.Hrefs.Should().Equal(bridge.ResourcePath(1), bridge.ResourcePath(3));
        Ics.Value(report.CalendarData(bridge.ResourcePath(3))!, "SUMMARY").Should().Be("Third");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-10")]
    public async Task Multiget_UnknownHref_IsAnsweredWithNotFoundPropstatInsteadOfResponseStatus()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var unknown = bridge.ResourcePath(999);

        var report = await bridge.CreateClient().ReportAsync(bridge.CalendarPath(), Dav.CalendarMultiget([unknown]));

        // RFC 4791 7.9 expects <d:status>HTTP/1.1 404 Not Found</d:status> directly in the response.
        var response = report.ResponseFor(unknown)!;
        response.Element(Dav.D + "status").Should().BeNull();
        report.IsNotFound(unknown, Dav.D + "getetag").Should().BeTrue();
        report.IsNotFound(unknown, Dav.C + "calendar-data").Should().BeTrue();
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("")]
    [InlineData("<not xml")]
    public async Task Report_EmptyOrInvalidBody_Returns400(string body)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().ReportAsync(bridge.CalendarPath(), body);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Report_UnsupportedReportType_Returns501()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        const string freeBusy = """<?xml version="1.0"?><c:free-busy-query xmlns:c="urn:ietf:params:xml:ns:caldav"><c:time-range start="20250615T000000Z" end="20250616T000000Z"/></c:free-busy-query>""";

        var response = await bridge.CreateClient().ReportAsync(bridge.CalendarPath(), freeBusy);

        response.Status.Should().Be(HttpStatusCode.NotImplemented);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task SyncCollection_InitialSync_ReturnsAllMembersAndASyncToken()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.Simple(2, "Second"));
        var bridge = await StartBridgeAsync();

        var report = await bridge.CreateClient().ReportAsync(bridge.CalendarPath(), Dav.SyncCollection());

        report.Status.Should().Be((HttpStatusCode)207);
        report.Hrefs.Should().BeEquivalentTo(bridge.ResourcePath(1), bridge.ResourcePath(2));
        report.Xml.Root!.Element(Dav.D + "sync-token")!.Value.Should().NotBeNullOrEmpty();
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-03")]
    public async Task SyncCollection_WithEarlierToken_IgnoresTheTokenAndNeverReportsRemovedMembers()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.Simple(2, "Second"));
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var calendar = bridge.CalendarPath();
        var first = await client.ReportAsync(calendar, Dav.SyncCollection());
        var token = first.Xml.Root!.Element(Dav.D + "sync-token")!.Value;
        var advertised = (await client.PropFindAsync(calendar, 0, Dav.D + "sync-token")).Property(calendar, Dav.D + "sync-token")!.Value;

        Donetick.WithChores(TestChoreFactory.Simple(2, "Second"));
        await bridge.RunPollAsync();
        var second = await client.ReportAsync(calendar, Dav.SyncCollection(token));

        token.Should().NotBe(advertised, "the REPORT token and the PROPFIND sync-token use different formats");
        second.Hrefs.Should().Equal([bridge.ResourcePath(2)], "the full list is returned and the removed chore is not reported");
        second.ResponseFor(bridge.ResourcePath(1)).Should().BeNull();
    }
}
