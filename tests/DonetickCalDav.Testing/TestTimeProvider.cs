using Microsoft.Extensions.Time.Testing;

namespace DonetickCalDav.Testing;

/// <summary>
/// A <see cref="FakeTimeProvider"/> that also records the timers the code under test creates.
/// </summary>
/// <remarks>
/// Advancing fake time only releases delays that already exist. If a test advances before the code
/// under test has started its delay, the delay starts from the new time and the test hangs. The
/// <c>WaitFor</c> and <c>AdvanceWhenScheduled</c> methods remove that race: they wait (in real time)
/// until a timer with the expected due time is pending, and only then move the clock.
/// </remarks>
public sealed class TestTimeProvider : FakeTimeProvider
{
    /// <summary>Start of fake time; same day as the fixed dates in <see cref="TestChoreFactory"/>.</summary>
    public static readonly DateTimeOffset DefaultStart = new(2025, 6, 15, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Real-time limit for waiting on a timer. Generous, because CI machines can be slow.</summary>
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(10);

    private readonly object _lock = new();
    private readonly List<PendingTimer> _timers = [];

    public TestTimeProvider() : this(DefaultStart) { }

    public TestTimeProvider(DateTimeOffset start) : base(start) { }

    /// <summary>Due times of timers that have been created and have not fired yet.</summary>
    public IReadOnlyList<TimeSpan> PendingDueTimes
    {
        get
        {
            lock (_lock)
            {
                RemoveFired();
                return _timers.Select(t => t.DueTime).ToList();
            }
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = base.CreateTimer(callback, state, dueTime, period);
        if (dueTime != Timeout.InfiniteTimeSpan)
        {
            lock (_lock)
                _timers.Add(new PendingTimer(GetUtcNow() + dueTime, dueTime));
        }

        return timer;
    }

    /// <summary>Waits until a timer with exactly <paramref name="dueTime"/> is pending.</summary>
    public async Task WaitForTimerAsync(TimeSpan dueTime, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultWaitTimeout);
        while (true)
        {
            lock (_lock)
            {
                RemoveFired();
                if (_timers.Any(t => t.DueTime == dueTime))
                    return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"No timer of {dueTime} was scheduled within {timeout ?? DefaultWaitTimeout}. " +
                    $"Pending: [{string.Join(", ", PendingDueTimes)}].");
            }

            await Task.Delay(10, ct);
        }
    }

    /// <summary>Waits until a timer with <paramref name="dueTime"/> is pending, then advances by that amount.</summary>
    public async Task AdvanceWhenScheduledAsync(TimeSpan dueTime, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        await WaitForTimerAsync(dueTime, timeout, ct);
        Advance(dueTime);
    }

    /// <summary>Advances through each delay in turn, waiting for each to be scheduled first.</summary>
    public async Task AdvanceThroughAsync(IEnumerable<TimeSpan> dueTimes, CancellationToken ct = default)
    {
        foreach (var dueTime in dueTimes)
            await AdvanceWhenScheduledAsync(dueTime, ct: ct);
    }

    // Timers whose fire time has passed are no longer pending. Cancelled delays stay in the list until
    // their fire time; that only matters for diagnostics.
    private void RemoveFired()
    {
        var now = GetUtcNow();
        _timers.RemoveAll(t => t.FiresAt <= now);
    }

    private sealed record PendingTimer(DateTimeOffset FiresAt, TimeSpan DueTime);
}
