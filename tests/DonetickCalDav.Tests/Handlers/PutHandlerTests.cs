using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Handlers;
using DonetickCalDav.Configuration;
using DonetickCalDav.Donetick;
using DonetickCalDav.Donetick.Models;
using DonetickCalDav.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DonetickCalDav.Tests.Handlers;

public class PutHandlerTests
{
    private readonly ChoreCache _cache = new(NullLogger<ChoreCache>.Instance);
    private readonly IDonetickApiClient _client = Substitute.For<IDonetickApiClient>();
    private readonly PutHandler _handler;

    private static readonly AppSettings TestSettings = new()
    {
        CalDav = new CalDavSettings { Username = "testuser" },
    };

    public PutHandlerTests()
    {
        // Default: GetAllChoresAsync returns empty list (for cache refresh after update)
        _client.GetAllChoresAsync(Arg.Any<CancellationToken>())
            .Returns(new List<DonetickChore>());

        _handler = new PutHandler(
            _cache,
            _client,
            Options.Create(TestSettings),
            NullLogger<PutHandler>.Instance);
    }

    // ── Update existing chore ───────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_UpdateExistingChore_CallsUpdateApi()
    {
        _cache.UpsertChore(TestChoreFactory.Simple(42));

        var ics = BuildVTodoIcs("donetick-42@donetick", "Updated task", "NEEDS-ACTION");
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/donetick-42.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(204);
        await _client.Received(1).UpdateChoreAsync(42,
            Arg.Is<ChoreLiteRequest>(r => r.Name == "Updated task"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CompleteExistingChore_CallsCompleteApi()
    {
        _cache.UpsertChore(TestChoreFactory.Simple(42));

        var ics = BuildVTodoIcs("donetick-42@donetick", "Done task", "COMPLETED");
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/donetick-42.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(204);
        await _client.Received(1).CompleteChoreAsync(42, Arg.Any<CancellationToken>());
        await _client.DidNotReceive().UpdateChoreAsync(Arg.Any<int>(),
            Arg.Any<ChoreLiteRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_CompleteExistingChore_OmitsETag()
    {
        // After completing a recurring chore, the server resets status to NEEDS-ACTION
        // and advances NextDueDate. Omitting ETag forces the client to refetch on next
        // sync so it picks up the new state instead of keeping its local COMPLETED version.
        _cache.UpsertChore(TestChoreFactory.Simple(42));

        var ics = BuildVTodoIcs("donetick-42@donetick", "Done task", "COMPLETED");
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/donetick-42.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(204);
        context.Response.Headers.ContainsKey("ETag").Should().BeFalse(
            "ETag must be omitted after completion to force client refetch");
    }

    [Fact]
    public async Task HandleAsync_UpdateExistingChore_IncludesETag()
    {
        // Normal updates (non-completion) should include ETag
        var chore = TestChoreFactory.Simple(42);
        _cache.UpsertChore(chore);

        // The refresh after update returns the chore so it stays in cache
        _client.GetAllChoresAsync(Arg.Any<CancellationToken>())
            .Returns(new List<DonetickChore> { chore });

        var ics = BuildVTodoIcs("donetick-42@donetick", "Updated task", "NEEDS-ACTION");
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/donetick-42.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(204);
        context.Response.Headers.ContainsKey("ETag").Should().BeTrue(
            "normal updates should include ETag for client caching");
    }

    // ── Create new chore ────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_NewChore_CallsCreateApiAndMapsUid()
    {
        var createdChore = TestChoreFactory.Simple(99, "New task");
        _client.CreateChoreAsync(Arg.Any<ChoreLiteRequest>(), Arg.Any<CancellationToken>())
            .Returns(createdChore);

        var uid = "APPLE-UUID-1234-5678-9ABC-DEF012345678";
        var ics = BuildVTodoIcs(uid, "New task", "NEEDS-ACTION");
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/APPLE-UUID-1234-5678-9ABC-DEF012345678.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(201);
        context.Response.Headers["Location"].ToString()
            .Should().Contain("donetick-99.ics");
        await _client.Received(1).CreateChoreAsync(
            Arg.Is<ChoreLiteRequest>(r => r.Name == "New task"),
            Arg.Any<CancellationToken>());

        // UID should be mapped in cache for future lookups
        _cache.GetIdByUid(uid).Should().Be(99);
    }

    // ── Duplicate prevention ────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_CompletedVTodoWithUnknownUid_Returns204WithoutCreating()
    {
        // Apple sends COMPLETED VTODO with a new UUID for recurring task completions.
        // We should NOT create a new chore for this.
        var ics = BuildVTodoIcs("UNKNOWN-UUID-ABCD-1234-5678-ABCDEF012345", "Recurring done", "COMPLETED");
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/UNKNOWN-UUID-ABCD-1234-5678-ABCDEF012345.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(204);
        await _client.DidNotReceive().CreateChoreAsync(
            Arg.Any<ChoreLiteRequest>(), Arg.Any<CancellationToken>());
        await _client.DidNotReceive().CompleteChoreAsync(
            Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    // ── Error handling ──────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_InvalidIcsBody_Returns400()
    {
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/donetick-42.ics", "this is not valid ICS");

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task HandleAsync_ApiError_Returns502()
    {
        _cache.UpsertChore(TestChoreFactory.Simple(42));
        _client.UpdateChoreAsync(42, Arg.Any<ChoreLiteRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<DonetickChore?>(new HttpRequestException("API down")));

        var ics = BuildVTodoIcs("donetick-42@donetick", "Task", "NEEDS-ACTION");
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/donetick-42.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task HandleAsync_CreateReturnsNull_Returns502()
    {
        _client.CreateChoreAsync(Arg.Any<ChoreLiteRequest>(), Arg.Any<CancellationToken>())
            .Returns((DonetickChore?)null);

        var ics = BuildVTodoIcs("new-uid-1234", "New task", "NEEDS-ACTION");
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/new-uid-1234.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(502);
    }

    // ── UID resolution ──────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_ResolvesByDonetickUidPattern()
    {
        // UID pattern donetick-{id}@donetick should resolve without cache
        _cache.UpsertChore(TestChoreFactory.Simple(7));

        var ics = BuildVTodoIcs("donetick-7@donetick", "Task seven", "NEEDS-ACTION");
        // Path does NOT contain donetick-7 — resolution happens via UID pattern
        var context = HttpContextHelper.Create("PUT",
            "/caldav/calendars/testuser/tasks/some-other-path.ics", ics);

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(204);
        await _client.Received(1).UpdateChoreAsync(7,
            Arg.Any<ChoreLiteRequest>(), Arg.Any<CancellationToken>());
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildVTodoIcs(string uid, string summary, string status) =>
        $"""
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Test//Test//EN
        BEGIN:VTODO
        UID:{uid}
        SUMMARY:{summary}
        STATUS:{status}
        END:VTODO
        END:VCALENDAR
        """;
}
