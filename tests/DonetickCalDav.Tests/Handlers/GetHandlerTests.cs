using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Handlers;
using DonetickCalDav.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace DonetickCalDav.Tests.Handlers;

public class GetHandlerTests
{
    private readonly ChoreCache _cache = new(NullLogger<ChoreCache>.Instance);
    private readonly GetHandler _handler;

    public GetHandlerTests()
    {
        _handler = new GetHandler(_cache, NullLogger<GetHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_DonetickPath_Returns200WithIcsBody()
    {
        var chore = TestChoreFactory.Simple(42, "Test task");
        _cache.UpsertChore(chore);

        var context = HttpContextHelper.Create("GET", "/caldav/calendars/user/tasks/donetick-42.ics");

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(200);
        context.Response.ContentType.Should().Be("text/calendar; charset=utf-8");
        context.Response.Headers["ETag"].ToString().Should().NotBeNullOrEmpty();

        var body = await HttpContextHelper.ReadResponseBodyAsync(context);
        body.Should().Contain("BEGIN:VTODO");
        body.Should().Contain("SUMMARY:Test task");
    }

    [Fact]
    public async Task HandleAsync_UuidPath_WithMapping_Returns200()
    {
        var chore = TestChoreFactory.Simple(42, "Mapped task");
        _cache.UpsertChore(chore);
        _cache.MapUid("B6BB5B67-1234-5678-9ABC-DEF012345678", 42);

        var context = HttpContextHelper.Create("GET",
            "/caldav/calendars/user/tasks/B6BB5B67-1234-5678-9ABC-DEF012345678.ics");

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(200);
        var body = await HttpContextHelper.ReadResponseBodyAsync(context);
        body.Should().Contain("SUMMARY:Mapped task");
    }

    [Fact]
    public async Task HandleAsync_UnknownPath_Returns404()
    {
        var context = HttpContextHelper.Create("GET", "/caldav/calendars/user/tasks/");

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task HandleAsync_KnownPathButNotInCache_Returns404()
    {
        // ID 99 not in cache
        var context = HttpContextHelper.Create("GET", "/caldav/calendars/user/tasks/donetick-99.ics");

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(404);
    }
}
