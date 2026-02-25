using DonetickCalDav.Cache;
using DonetickCalDav.CalDav.Handlers;
using DonetickCalDav.Donetick;
using DonetickCalDav.Tests.Helpers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace DonetickCalDav.Tests.Handlers;

public class DeleteHandlerTests
{
    private readonly ChoreCache _cache = new(NullLogger<ChoreCache>.Instance);
    private readonly IDonetickApiClient _client = Substitute.For<IDonetickApiClient>();
    private readonly DeleteHandler _handler;

    public DeleteHandlerTests()
    {
        _handler = new DeleteHandler(_cache, _client, NullLogger<DeleteHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_ValidPath_Returns204AndDeletesFromApi()
    {
        _cache.UpsertChore(TestChoreFactory.Simple(42));

        var context = HttpContextHelper.Create("DELETE", "/caldav/calendars/user/tasks/donetick-42.ics");

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(204);
        await _client.Received(1).DeleteChoreAsync(42, Arg.Any<CancellationToken>());
        _cache.GetChore(42).Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_UnknownPath_Returns404()
    {
        var context = HttpContextHelper.Create("DELETE", "/caldav/calendars/user/tasks/");

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(404);
        await _client.DidNotReceive().DeleteChoreAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ApiError_Returns502()
    {
        _cache.UpsertChore(TestChoreFactory.Simple(42));
        _client.DeleteChoreAsync(42, Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("API error")));

        var context = HttpContextHelper.Create("DELETE", "/caldav/calendars/user/tasks/donetick-42.ics");

        await _handler.HandleAsync(context);

        context.Response.StatusCode.Should().Be(502);
    }
}
