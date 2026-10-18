using System.Net;
using System.Text.Json;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Writes;

/// <summary>IT-07: editing a to-do. Only name, description and due date reach Donetick.</summary>
[Trait("Scenario", "IT-07")]
[Trait("Covers", "UC-04")]
[Trait("Covers", "FR-12")]
public class EditTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    public async Task Edit_CanonicalPath_SendsNameDescriptionAndDateAndReturnsTheNewETag()
    {
        var original = TestChoreFactory.WithDueDate(7);
        var updated = TestChoreFactory.WithDueDate(7, new DateTime(2025, 6, 21, 10, 0, 0, DateTimeKind.Utc));
        updated.Name = "Renamed";
        updated.Description = "New notes";
        updated.UpdatedAt = original.UpdatedAt.AddMinutes(10);
        Donetick.WithChores(original).OnUpdate(updated);
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var calendar = bridge.CalendarPath();
        var oldETag = (await client.GetAsync(bridge.ResourcePath(7))).ETag;
        var oldCTag = (await client.PropFindAsync(calendar, 0, Dav.CS + "getctag")).Property(calendar, Dav.CS + "getctag")!.Value;

        var response = await client.PutAsync(bridge.ResourcePath(7),
            Ics.VTodo(Ics.ChoreUid(7), "Renamed", description: "New notes", dueDate: new DateOnly(2025, 6, 21)));

        response.Status.Should().Be(HttpStatusCode.NoContent);
        var put = Donetick.Received("PUT", $"{DonetickStub.ChoresPath}/7").Should().ContainSingle().Subject;
        JsonDocument.Parse(put.Body!).RootElement.GetRawText().Should().Be(
            """{"name":"Renamed","description":"New notes","dueDate":"2025-06-21"}""");

        var get = await client.GetAsync(bridge.ResourcePath(7));
        response.ETag.Should().NotBe(oldETag).And.Be(get.ETag, "the ETag in the PUT response matches the stored version");
        Ics.Value(get.Body, "SUMMARY").Should().Be("Renamed");
        var newCTag = (await client.PropFindAsync(calendar, 0, Dav.CS + "getctag")).Property(calendar, Dav.CS + "getctag")!.Value;
        newCTag.Should().NotBe(oldCTag);
        Donetick.Count("GET", DonetickStub.ChoresPath).Should().Be(2, "an edit updates the cache from the PUT response, without a reload");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Edit_NonCanonicalPathWithBridgeUid_IsRoutedByUid()
    {
        Donetick.WithChores(TestChoreFactory.Simple(7)).OnUpdate(TestChoreFactory.Simple(7, "Renamed"));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().PutAsync(bridge.CalendarPath() + "renamed-by-client.ics",
            Ics.VTodo(Ics.ChoreUid(7), "Renamed"));

        response.Status.Should().Be(HttpStatusCode.NoContent);
        Donetick.Count("PUT", $"{DonetickStub.ChoresPath}/7").Should().Be(1);
        Donetick.Count("POST", DonetickStub.ChoresPath).Should().Be(0);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-09")]
    public async Task Edit_ClearedDescriptionAndDueDate_AreNotSentSoDonetickKeepsThem()
    {
        Donetick.WithChores(TestChoreFactory.WithDueDate(7)).OnUpdate(TestChoreFactory.WithDueDate(7));
        var bridge = await StartBridgeAsync();

        await bridge.CreateClient().PutAsync(bridge.ResourcePath(7), Ics.VTodo(Ics.ChoreUid(7), "Chore with due date"));

        var body = JsonDocument.Parse(Donetick.Received("PUT", $"{DonetickStub.ChoresPath}/7").Single().Body!).RootElement;
        body.GetRawText().Should().Be("""{"name":"Chore with due date"}""",
            "null fields are omitted and Donetick keeps the old values (contract check CT-04)");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-08")]
    public async Task Edit_StaleIfMatch_IsIgnoredAndTheLastWriterWins()
    {
        Donetick.WithChores(TestChoreFactory.Simple(7)).OnUpdate(TestChoreFactory.Simple(7, "Mine"));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().SendAsync(HttpMethod.Put, bridge.ResourcePath(7),
            Ics.VTodo(Ics.ChoreUid(7), "Mine"), "text/calendar",
            configure: r => r.Headers.TryAddWithoutValidation("If-Match", "\"0000000000000000\""));

        response.Status.Should().Be(HttpStatusCode.NoContent, "no precondition check: 412 would be correct");
    }
}
