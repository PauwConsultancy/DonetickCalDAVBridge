using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Writes;

/// <summary>
/// IT-08: ticking off a to-do. For a recurring task Apple sends two PUTs: a COMPLETED copy under a new
/// UUID, and the original resource with STATUS:COMPLETED. Only the second may reach Donetick.
/// </summary>
[Trait("Scenario", "IT-08")]
[Trait("Covers", "UC-05")]
[Trait("Covers", "FR-13")]
[Trait("Covers", "FR-14")]
public class CompleteTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    private const string CopyUid = "D3C4B5A6-1111-4222-8333-944455556666";

    [Fact(Timeout = DefaultTimeout)]
    public async Task Complete_RecurringChore_CompletesOnceReloadsAndOmitsTheETag()
    {
        var daily = TestChoreFactory.DailyRecurring(3);
        Donetick.WithChores(daily).OnComplete(3);
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var listCallsBefore = Donetick.Count("GET", DonetickStub.ChoresPath);

        // What Donetick returns after the completion: the next occurrence, a day later.
        var advanced = TestChoreFactory.DailyRecurring(3);
        advanced.NextDueDate = daily.NextDueDate!.Value.AddDays(1);
        advanced.UpdatedAt = daily.UpdatedAt.AddHours(1);
        Donetick.WithChores(advanced);

        var response = await client.PutAsync(bridge.ResourcePath(3), Ics.VTodo(Ics.ChoreUid(3), "Daily chore", status: "COMPLETED"));

        response.Status.Should().Be(HttpStatusCode.NoContent);
        response.ETag.Should().BeNull("without an ETag the client refetches and picks up the next due date");
        Donetick.Count("POST", $"{DonetickStub.ChoresPath}/3/complete").Should().Be(1);
        Donetick.Count("GET", DonetickStub.ChoresPath).Should().Be(listCallsBefore + 1, "a completion reloads the whole list");

        var get = await client.GetAsync(bridge.ResourcePath(3));
        Ics.Line(get.Body, "DUE").Should().Be("DUE:20250617T100000Z");
        Ics.Value(get.Body, "STATUS").Should().Be("NEEDS-ACTION");
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("Covers", "BR-14")]
    public async Task Complete_ApplesExtraCompletedCopy_IsAcceptedWithoutAnyDonetickCall()
    {
        Donetick.WithChores(TestChoreFactory.DailyRecurring(3)).OnComplete(3);
        var bridge = await StartBridgeAsync();
        var requestsBefore = Donetick.AllRequests.Count;

        var response = await bridge.CreateClient().PutAsync(bridge.CalendarPath() + CopyUid + ".ics",
            Ics.VTodo(CopyUid, "Daily chore", status: "COMPLETED"));

        response.Status.Should().Be(HttpStatusCode.NoContent);
        Donetick.AllRequests.Should().HaveCount(requestsBefore, "no create and no completion for the copy");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Complete_AppleDoublePut_CompletesExactlyOnce()
    {
        Donetick.WithChores(TestChoreFactory.DailyRecurring(3)).OnComplete(3);
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();

        await client.PutAsync(bridge.CalendarPath() + CopyUid + ".ics", Ics.VTodo(CopyUid, "Daily chore", status: "COMPLETED"));
        await client.PutAsync(bridge.ResourcePath(3), Ics.VTodo(Ics.ChoreUid(3), "Daily chore", status: "COMPLETED"));

        Donetick.Count("POST", $"{DonetickStub.ChoresPath}/3/complete").Should().Be(1);
        Donetick.Count("POST", DonetickStub.ChoresPath).Should().Be(0);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Complete_OneOffChore_DisappearsAfterTheReload()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.WithDueDate(2)).OnComplete(2);
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        Donetick.WithChores(TestChoreFactory.Simple(1)); // Donetick drops completed one-off chores from the list (CT-03)

        await client.PutAsync(bridge.ResourcePath(2), Ics.VTodo(Ics.ChoreUid(2), "Chore with due date", status: "COMPLETED"));

        (await client.GetAsync(bridge.ResourcePath(2))).Status.Should().Be(HttpStatusCode.NotFound);
    }
}
