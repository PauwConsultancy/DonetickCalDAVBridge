using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Writes;

/// <summary>IT-09: deleting a to-do.</summary>
[Trait("Scenario", "IT-09")]
[Trait("Covers", "UC-06")]
[Trait("Covers", "FR-15")]
public class DeleteTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    public async Task Delete_KnownChore_DeletesInDonetickAndRemovesItFromTheCalendar()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.Simple(5, "To delete")).OnDelete(5);
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var calendar = bridge.CalendarPath();
        var oldCTag = (await client.PropFindAsync(calendar, 0, Dav.CS + "getctag")).Property(calendar, Dav.CS + "getctag")!.Value;

        var response = await client.DeleteAsync(bridge.ResourcePath(5));

        response.Status.Should().Be(HttpStatusCode.NoContent);
        Donetick.Count("DELETE", $"{DonetickStub.ChoresPath}/5").Should().Be(1);
        var members = await client.PropFindAsync(calendar, 1, Dav.CS + "getctag");
        members.Hrefs.Should().BeEquivalentTo(calendar, bridge.ResourcePath(1));
        members.Property(calendar, Dav.CS + "getctag")!.Value.Should().NotBe(oldCTag);
        (await client.GetAsync(bridge.ResourcePath(5))).Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Delete_UnresolvablePath_Returns404WithoutCallingDonetick()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var requestsBefore = Donetick.AllRequests.Count;

        var response = await bridge.CreateClient().DeleteAsync(bridge.CalendarPath() + "3F2504E0-4F89-11D3-9A0C-0305E82C3301.ics");

        response.Status.Should().Be(HttpStatusCode.NotFound);
        Donetick.AllRequests.Should().HaveCount(requestsBefore);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-25")]
    public async Task Delete_ChoreAlreadyGoneInDonetick_Returns502()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        Donetick.FailAlways("DELETE", $"{DonetickStub.ChoresPath}/1", statusCode: 404);
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().DeleteAsync(bridge.ResourcePath(1));

        // A client that retries a delete after a lost response gets an error instead of 404 or 204.
        response.Status.Should().Be(HttpStatusCode.BadGateway);
        Donetick.Count("DELETE", $"{DonetickStub.ChoresPath}/1").Should().Be(1, "4xx answers are not retried");
    }
}
