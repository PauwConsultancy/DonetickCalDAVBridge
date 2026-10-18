using System.Net;
using System.Text.Json;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Writes;

/// <summary>
/// IT-06: creating a to-do in Reminders. Apple PUTs to a path named after its own UUID; the bridge
/// creates the chore in Donetick and keeps a UID map so later requests on that path still work.
/// Uses the reference mappings, whose create mapping echoes the request as chore 900.
/// </summary>
[Trait("Scenario", "IT-06")]
[Trait("Covers", "UC-03")]
[Trait("Covers", "FR-11")]
[Trait("Covers", "FR-16")]
public class CreateTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    private const string AppleUid = "6B29FC40-CA47-1067-B31D-00DD010662DA";

    [Fact(Timeout = DefaultTimeout)]
    public async Task Create_FromAppleUuid_PostsToDonetickAndAnswers201WithLocationAndETag()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();
        var path = bridge.CalendarPath() + AppleUid + ".ics";

        var response = await bridge.CreateClient().PutAsync(path,
            Ics.VTodo(AppleUid, "Repaint the fence", description: "Before summer", dueDate: new DateOnly(2025, 6, 20)));

        response.Status.Should().Be(HttpStatusCode.Created);
        response.Header("Location").Should().Be(bridge.ResourcePath(ReferenceData.CreatedChoreId));
        response.ETag.Should().MatchRegex("^\"[0-9A-F]{16}\"$");

        var post = Donetick.Received("POST", DonetickStub.ChoresPath).Should().ContainSingle().Subject;
        JsonDocument.Parse(post.Body!).RootElement.GetRawText().Should().Be(
            """{"name":"Repaint the fence","description":"Before summer","dueDate":"2025-06-20"}""");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Create_ThenGetPutAndDeleteOnTheUuidPath_ReachTheSameChore()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var path = bridge.CalendarPath() + AppleUid + ".ics";
        await client.PutAsync(path, Ics.VTodo(AppleUid, "Repaint the fence"));

        var get = await client.GetAsync(path);
        var edit = await client.PutAsync(path, Ics.VTodo(AppleUid, "Repaint the fence white"));
        var delete = await client.DeleteAsync(path);

        get.Status.Should().Be(HttpStatusCode.OK);
        Ics.Value(get.Body, "UID").Should().Be(Ics.ChoreUid(ReferenceData.CreatedChoreId), "the bridge serves its own UID");
        Ics.Value(get.Body, "SUMMARY").Should().Be("Repaint the fence");
        edit.Status.Should().Be(HttpStatusCode.NoContent);
        delete.Status.Should().Be(HttpStatusCode.NoContent);

        var choreUrl = $"{DonetickStub.ChoresPath}/{ReferenceData.CreatedChoreId}";
        Donetick.Count("POST", DonetickStub.ChoresPath).Should().Be(1, "the second PUT is an edit, not a second create");
        Donetick.Count("PUT", choreUrl).Should().Be(1);
        Donetick.Count("DELETE", choreUrl).Should().Be(1);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Create_InLabelList_LandsInTheDefaultList()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = true);
        var client = bridge.CreateClient();

        var response = await client.PutAsync(bridge.CalendarPath("home") + AppleUid + ".ics", Ics.VTodo(AppleUid, "Fix the tap"));

        // The eAPI cannot set labels, so the new chore has none and belongs to the default list.
        response.Header("Location").Should().Be(bridge.ResourcePath(ReferenceData.CreatedChoreId, "tasks"));
        var home = await client.PropFindAsync(bridge.CalendarPath("home"), 1, Dav.D + "getetag");
        home.Hrefs.Should().NotContain(h => h.Contains($"donetick-{ReferenceData.CreatedChoreId}"));
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-09")]
    public async Task Create_DueWithTimeOfDay_SendsTheDateOnly()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();

        await bridge.CreateClient().PutAsync(bridge.CalendarPath() + AppleUid + ".ics",
            Ics.VTodo(AppleUid, "Dentist", dueDateTimeUtc: new DateTime(2025, 6, 20, 14, 30, 0, DateTimeKind.Utc)));

        var body = JsonDocument.Parse(Donetick.Received("POST", DonetickStub.ChoresPath).Single().Body!).RootElement;
        body.GetProperty("dueDate").GetString().Should().Be("2025-06-20", "the time of day is dropped (F-09)");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Create_WithoutSummary_UsesUntitledTask()
    {
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync();
        var ics = Ics.VTodo(AppleUid, "placeholder").Replace("SUMMARY:placeholder\r\n", "");

        var response = await bridge.CreateClient().PutAsync(bridge.CalendarPath() + AppleUid + ".ics", ics);

        response.Status.Should().Be(HttpStatusCode.Created);
        var body = JsonDocument.Parse(Donetick.Received("POST", DonetickStub.ChoresPath).Single().Body!).RootElement;
        body.GetProperty("name").GetString().Should().Be("Untitled Task");
        body.TryGetProperty("description", out _).Should().BeFalse("empty fields are omitted");
        body.TryGetProperty("dueDate", out _).Should().BeFalse();
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Create_DonetickAnswersNull_Returns502()
    {
        Donetick.WithReferenceData();
        Donetick.Server.Given(WireMock.RequestBuilders.Request.Create().WithPath(DonetickStub.ChoresPath).UsingPost())
            .AtPriority(1)
            .RespondWith(WireMock.ResponseBuilders.Response.Create().WithStatusCode(200).WithBody("null"));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().PutAsync(bridge.CalendarPath() + AppleUid + ".ics", Ics.VTodo(AppleUid, "Lost"));

        response.Status.Should().Be(HttpStatusCode.BadGateway);
    }
}
