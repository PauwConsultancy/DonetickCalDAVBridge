using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;
using Microsoft.Extensions.Logging;

namespace DonetickCalDav.IntegrationTests.Concurrency;

/// <summary>
/// IT-15: parallel reads and writes through the whole pipeline. The Ical.Net serializer race showed
/// that unit tests alone do not catch shared-state defects.
/// </summary>
[Trait("Scenario", "IT-15")]
[Trait("Covers", "R5")]
public class ConcurrencyTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = 60_000)]
    public async Task ParallelGetsReportsAndEdits_ProduceNoErrorsAndAConsistentCache()
    {
        var chores = Enumerable.Range(1, 30).Select(i => TestChoreFactory.WithDueDate(i)).ToArray();
        Donetick.WithChores(chores);
        foreach (var chore in chores.Take(10))
        {
            var updated = TestChoreFactory.WithDueDate(chore.Id);
            updated.Name = $"Edited {chore.Id}";
            updated.UpdatedAt = chore.UpdatedAt.AddMinutes(1);
            Donetick.OnUpdate(updated);
        }

        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var calendar = bridge.CalendarPath();

        var work = new List<Task<DavResponse>>();
        for (var round = 0; round < 5; round++)
        {
            work.AddRange(chores.Select(c => client.GetAsync(bridge.ResourcePath(c.Id))));
            work.Add(client.ReportAsync(calendar, Dav.CalendarQuery()));
            work.Add(client.PropFindAsync(calendar, 1, Dav.D + "getetag", Dav.CS + "getctag"));
        }

        work.AddRange(chores.Take(10).Select(c =>
            client.PutAsync(bridge.ResourcePath(c.Id), Ics.VTodo(Ics.ChoreUid(c.Id), $"Edited {c.Id}"))));

        var responses = await Task.WhenAll(work);

        responses.Should().OnlyContain(r => r.Status == HttpStatusCode.OK || r.Status == (HttpStatusCode)207
                                            || r.Status == HttpStatusCode.NoContent);
        bridge.Logs.Entries.Should().NotContain(e => e.Level >= LogLevel.Error);

        var final = await client.ReportAsync(calendar, Dav.CalendarQuery());
        final.Hrefs.Should().HaveCount(30);
        foreach (var chore in chores.Take(10))
        {
            var get = await client.GetAsync(bridge.ResourcePath(chore.Id));
            Ics.Value(get.Body, "SUMMARY").Should().Be($"Edited {chore.Id}");
            final.GetETag(bridge.ResourcePath(chore.Id)).Should().Be(get.ETag);
        }
    }
}
