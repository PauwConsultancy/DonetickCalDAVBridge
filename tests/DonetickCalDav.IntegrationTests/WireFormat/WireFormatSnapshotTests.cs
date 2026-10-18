using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using DonetickCalDav.Testing.Snapshots;

namespace DonetickCalDav.IntegrationTests.WireFormat;

/// <summary>
/// SN-02 to SN-05: complete HTTP responses as approved files, with Apple-style requests against the
/// reference data. A change in status, headers or XML fails here and has to be approved explicitly.
/// Approve locally with SNAPSHOT_ACCEPT=1 and review the changed .verified files in the commit.
/// </summary>
[Trait("Level", "Snapshot")]
public class WireFormatSnapshotTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    private static readonly string[] DavHeaders = ["Content-Type", "DAV"];

    public static TheoryData<string, string, int, string, bool> PropFindLevels => new()
    {
        // name, path (relative to the user), depth, body, GroupByLabel
        { "root", "/caldav/", 0, AppleRequests.CurrentUserPrincipal, false },
        { "principal", "/caldav/principals/tester/", 0, AppleRequests.PrincipalProperties, false },
        { "home-depth0", "/caldav/calendars/tester/", 0, AppleRequests.HomeListing, false },
        { "home-depth1", "/caldav/calendars/tester/", 1, AppleRequests.HomeListing, false },
        { "home-depth1-by-label", "/caldav/calendars/tester/", 1, AppleRequests.HomeListing, true },
        { "collection-depth0", "/caldav/calendars/tester/tasks/", 0, AppleRequests.CollectionChangeCheck, false },
        { "collection-depth1", "/caldav/calendars/tester/tasks/", 1, AppleRequests.CollectionMembers, false },
        { "label-collection-depth1", "/caldav/calendars/tester/home/", 1, AppleRequests.CollectionMembers, true },
        { "resource", "/caldav/calendars/tester/tasks/donetick-103.ics", 0, AppleRequests.ResourceProperties, false },
        { "resource-by-label", "/caldav/calendars/tester/home/donetick-103.ics", 0, AppleRequests.ResourceProperties, true },
        { "allprop", "/caldav/calendars/tester/tasks/", 0, "", false },
    };

    [Theory(Timeout = DefaultTimeout)]
    [MemberData(nameof(PropFindLevels))]
    [Trait("Scenario", "SN-02")]
    public async Task PropFind_EachLevel_MatchesApprovedResponse(string name, string path, int depth, string body, bool groupByLabel)
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = groupByLabel);

        var response = await bridge.CreateClient().SendAsync(new HttpMethod("PROPFIND"), path,
            body.Length == 0 ? null : body, "application/xml", depth);

        Snapshot.Match(response.ToSnapshot(DavHeaders), "txt", discriminator: name, scrubbers: Scrubbers.Http);
    }

    public static TheoryData<string, string, string> Reports => new()
    {
        { "calendar-query", "/caldav/calendars/tester/tasks/", AppleRequests.OpenTodosQuery },
        { "calendar-query-label", "/caldav/calendars/tester/finance/", AppleRequests.OpenTodosQuery },
        {
            "calendar-multiget", "/caldav/calendars/tester/tasks/",
            AppleRequests.Multiget("/caldav/calendars/tester/tasks/", "/caldav/calendars/tester/tasks/donetick-101.ics",
                "/caldav/calendars/tester/tasks/donetick-105.ics", "/caldav/calendars/tester/tasks/donetick-999.ics")
        },
        { "sync-collection", "/caldav/calendars/tester/tasks/", AppleRequests.SyncCollection() },
    };

    [Theory(Timeout = DefaultTimeout)]
    [MemberData(nameof(Reports))]
    [Trait("Scenario", "SN-03")]
    public async Task Report_EachType_MatchesApprovedResponse(string name, string path, string body)
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = name.EndsWith("-label"));

        var response = await bridge.CreateClient().ReportAsync(path, body);

        Snapshot.Match(response.ToSnapshot(DavHeaders), "txt", discriminator: name, scrubbers: Scrubbers.Http);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("Scenario", "SN-04")]
    public async Task ErrorResponses_MatchApprovedHeadersAndBodies()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var calendar = bridge.CalendarPath();
        var headers = new[] { "Content-Type", "DAV", "WWW-Authenticate", "Allow", "Location" };

        var responses = new (string Name, DavResponse Response)[]
        {
            ("well-known", await client.Anonymous().PropFindAsync("/.well-known/caldav", 0)),
            ("options", await client.Anonymous().OptionsAsync("/caldav/")),
            ("unauthenticated", await client.Anonymous().PropFindAsync("/caldav/", 0)),
            ("move", await client.SendAsync(new HttpMethod("MOVE"), bridge.ResourcePath(ReferenceData.BuyGroceries))),
            ("mkcalendar", await client.SendAsync(new HttpMethod("MKCALENDAR"), bridge.CalendarPath("new"))),
            ("post", await client.SendAsync(HttpMethod.Post, calendar)),
            ("proppatch", await client.SendAsync(new HttpMethod("PROPPATCH"), calendar,
                Dav.PropPatch((Dav.Apple + "calendar-color", "#FF0000FF"), (Dav.D + "displayname", "Renamed")))),
            ("report-empty", await client.ReportAsync(calendar, "")),
            ("report-unsupported", await client.ReportAsync(calendar,
                """<?xml version="1.0"?><c:free-busy-query xmlns:c="urn:ietf:params:xml:ns:caldav"/>""")),
            ("get-unknown", await client.GetAsync(bridge.ResourcePath(999))),
        };

        var transcript = string.Join("\n", responses.Select(r => $"### {r.Name}\n{r.Response.ToSnapshot(headers)}\n"));
        Snapshot.Match(transcript, "txt", scrubbers: Scrubbers.Http);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("Scenario", "SN-05")]
    public async Task HealthJson_MatchesApprovedBody()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().Anonymous().GetAsync("/health/json");

        Snapshot.Match(response.ToSnapshot("Content-Type"), "txt", scrubbers: Scrubbers.Http);
    }
}
