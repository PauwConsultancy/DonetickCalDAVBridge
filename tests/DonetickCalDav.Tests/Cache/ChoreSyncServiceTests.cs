using DonetickCalDav.Cache;
using DonetickCalDav.Configuration;
using DonetickCalDav.Donetick;
using DonetickCalDav.Donetick.Models;
using DonetickCalDav.Testing;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DonetickCalDav.Tests.Cache;

/// <summary>
/// Timing of the background sync, driven by fake time. Each step waits for the next delay the
/// service schedules; that the expected delay appears at all is part of what is tested.
/// </summary>
public class ChoreSyncServiceTests : IAsyncLifetime
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);

    private readonly IDonetickApiClient _client = Substitute.For<IDonetickApiClient>();
    private readonly ChoreCache _cache = new(NullLogger<ChoreCache>.Instance);
    private readonly TestTimeProvider _time = new();
    private readonly ChoreSyncService _service;

    public ChoreSyncServiceTests()
    {
        var settings = Options.Create(new AppSettings
        {
            Donetick = new DonetickSettings
            {
                BaseUrl = "http://donetick.test",
                ApiKey = "key",
                PollIntervalSeconds = (int)PollInterval.TotalSeconds,
            },
        });
        _service = new ChoreSyncService(_client, _cache, settings, _time, NullLogger<ChoreSyncService>.Instance);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _service.StopAsync(CancellationToken.None);
        _service.Dispose();
    }

    [Fact]
    public async Task ExecuteAsync_DonetickAvailable_LoadsAfterStartupDelayAndThenPollsEveryInterval()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetAllChoresAsync(Arg.Any<CancellationToken>())
            .Returns([TestChoreFactory.Simple(1)], [TestChoreFactory.Simple(1)], [TestChoreFactory.Simple(1), TestChoreFactory.Simple(2)]);

        await _service.StartAsync(ct);
        await _time.AdvanceWhenScheduledAsync(ChoreSyncService.StartupDelay, ct: ct);
        await _time.WaitForTimerAsync(PollInterval, ct: ct);

        // Startup check plus the first loop iteration: two loads back to back (finding F-16).
        await _client.Received(2).GetAllChoresAsync(Arg.Any<CancellationToken>());
        _cache.GetAllChores().Should().HaveCount(1);

        await _time.AdvanceWhenScheduledAsync(PollInterval, ct: ct);
        await _time.WaitForTimerAsync(PollInterval, ct: ct);

        await _client.Received(3).GetAllChoresAsync(Arg.Any<CancellationToken>());
        _cache.GetAllChores().Should().HaveCount(2);
    }

    [Fact]
    public async Task ExecuteAsync_DonetickDownAtStartup_RetriesAfterThreeAndSixSecondsThenKeepsPolling()
    {
        var ct = TestContext.Current.CancellationToken;
        var calls = 0;
        _client.GetAllChoresAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            // Three startup attempts and the first loop iteration fail; the next poll succeeds.
            return ++calls <= 4
                ? Task.FromException<List<DonetickChore>>(new HttpRequestException("connection refused"))
                : Task.FromResult(new List<DonetickChore> { TestChoreFactory.Simple(1) });
        });

        await _service.StartAsync(ct);
        await _time.AdvanceWhenScheduledAsync(ChoreSyncService.StartupDelay, ct: ct);

        await _time.AdvanceWhenScheduledAsync(ChoreSyncService.StartupRetryDelay(1), ct: ct);
        ChoreSyncService.StartupRetryDelay(1).Should().Be(TimeSpan.FromSeconds(3));

        await _time.AdvanceWhenScheduledAsync(ChoreSyncService.StartupRetryDelay(2), ct: ct);
        ChoreSyncService.StartupRetryDelay(2).Should().Be(TimeSpan.FromSeconds(6));

        // After the third failed attempt the service gives up on startup and polls normally.
        await _time.WaitForTimerAsync(PollInterval, ct: ct);
        calls.Should().Be(4);
        _cache.GetAllChores().Should().BeEmpty();

        await _time.AdvanceWhenScheduledAsync(PollInterval, ct: ct);
        await _time.WaitForTimerAsync(PollInterval, ct: ct);

        calls.Should().Be(5);
        _cache.GetAllChores().Should().ContainSingle(c => c.Chore.Id == 1);
    }

    [Fact]
    public async Task ExecuteAsync_UnexpectedErrorDuringPoll_KeepsPreviousCacheAndContinues()
    {
        var ct = TestContext.Current.CancellationToken;
        var calls = 0;
        _client.GetAllChoresAsync(Arg.Any<CancellationToken>()).Returns(_ => ++calls switch
        {
            <= 2 => Task.FromResult(new List<DonetickChore> { TestChoreFactory.Simple(1) }),
            3 => Task.FromException<List<DonetickChore>>(new System.Text.Json.JsonException("unknown shape")),
            _ => Task.FromResult(new List<DonetickChore> { TestChoreFactory.Simple(1), TestChoreFactory.Simple(2) }),
        });

        await _service.StartAsync(ct);
        await _time.AdvanceWhenScheduledAsync(ChoreSyncService.StartupDelay, ct: ct);
        await _time.WaitForTimerAsync(PollInterval, ct: ct);

        await _time.AdvanceWhenScheduledAsync(PollInterval, ct: ct);
        await _time.WaitForTimerAsync(PollInterval, ct: ct);
        calls.Should().Be(3);
        _cache.GetAllChores().Should().ContainSingle("a failed poll keeps the previous cache");

        await _time.AdvanceWhenScheduledAsync(PollInterval, ct: ct);
        await _time.WaitForTimerAsync(PollInterval, ct: ct);
        _cache.GetAllChores().Should().HaveCount(2);
    }

    [Fact]
    public async Task StopAsync_DuringPollDelay_StopsWithoutFurtherCalls()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetAllChoresAsync(Arg.Any<CancellationToken>()).Returns([TestChoreFactory.Simple(1)]);

        await _service.StartAsync(ct);
        await _time.AdvanceWhenScheduledAsync(ChoreSyncService.StartupDelay, ct: ct);
        await _time.WaitForTimerAsync(PollInterval, ct: ct);

        await _service.StopAsync(ct);
        _time.Advance(PollInterval * 3);

        await _client.Received(2).GetAllChoresAsync(Arg.Any<CancellationToken>());
        _service.ExecuteTask!.IsCompleted.Should().BeTrue();
    }
}
