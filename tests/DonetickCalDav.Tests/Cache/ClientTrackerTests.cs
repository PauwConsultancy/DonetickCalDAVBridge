using DonetickCalDav.Cache;
using FluentAssertions;

namespace DonetickCalDav.Tests.Cache;

public class ClientTrackerTests
{
    // ── RecordRequest ────────────────────────────────────────────────────────

    [Fact]
    public void RecordRequest_AppleCalendar_TracksAsAppleCalendar()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("Mac+OS+X/15.3 (24D60) CalendarAgent/1111", "PROPFIND");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().ContainSingle()
            .Which.DisplayName.Should().Be("Apple Calendar (macOS)");
    }

    [Fact]
    public void RecordRequest_AppleReminders_TracksAsAppleReminders()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("Mac+OS+X/15.3 (24D60) remindd/1111", "REPORT");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().ContainSingle()
            .Which.DisplayName.Should().Be("Apple Reminders (macOS)");
    }

    [Fact]
    public void RecordRequest_IosClient_TracksAsIos()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("iOS/18.3 (22D60) dataaccessd/1.0", "PROPFIND");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().ContainSingle()
            .Which.DisplayName.Should().Be("Apple Calendar/Reminders (iOS)");
    }

    [Fact]
    public void RecordRequest_NullUserAgent_TracksAsUnknown()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest(null, "GET");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().ContainSingle()
            .Which.DisplayName.Should().Be("Unknown client");
    }

    [Fact]
    public void RecordRequest_Curl_TracksAsCurl()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("curl/8.7.1", "GET");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().ContainSingle()
            .Which.DisplayName.Should().Be("curl");
    }

    [Fact]
    public void RecordRequest_DAVx5_TracksAsAndroid()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("DAVx5/4.3.15-ose (2024/01/15; dav4jvm; okhttp/4.12.0) Android/14", "PROPFIND");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().ContainSingle()
            .Which.DisplayName.Should().Be("DAVx5 (Android)");
    }

    // ── Request counting ─────────────────────────────────────────────────────

    [Fact]
    public void RecordRequest_MultipleCalls_IncrementsRequestCount()
    {
        var tracker = new ClientTracker();
        var ua = "Mac+OS+X/15.3 (24D60) CalendarAgent/1111";

        tracker.RecordRequest(ua, "PROPFIND");
        tracker.RecordRequest(ua, "REPORT");
        tracker.RecordRequest(ua, "GET");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().ContainSingle()
            .Which.RequestCount.Should().Be(3);
    }

    [Fact]
    public void RecordRequest_DifferentVersionsSameClient_GroupedTogether()
    {
        var tracker = new ClientTracker();

        // Different versions of CalendarAgent should be grouped
        tracker.RecordRequest("Mac+OS+X/15.3 (24D60) CalendarAgent/1111", "PROPFIND");
        tracker.RecordRequest("Mac+OS+X/15.4 (24E50) CalendarAgent/1200", "PROPFIND");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().ContainSingle()
            .Which.RequestCount.Should().Be(2);
    }

    // ── Multiple clients ─────────────────────────────────────────────────────

    [Fact]
    public void GetActiveClients_MultipleDifferentClients_ReturnsAll()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("Mac+OS+X/15.3 (24D60) CalendarAgent/1111", "PROPFIND");
        tracker.RecordRequest("Mac+OS+X/15.3 (24D60) remindd/1111", "REPORT");
        tracker.RecordRequest("curl/8.7.1", "GET");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().HaveCount(3);

        var names = clients.Select(c => c.DisplayName).ToList();
        names.Should().Contain("Apple Calendar (macOS)");
        names.Should().Contain("Apple Reminders (macOS)");
        names.Should().Contain("curl");
    }

    // ── TotalRequests ────────────────────────────────────────────────────────

    [Fact]
    public void TotalRequests_SumsAcrossAllClients()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("curl/8.7.1", "GET");
        tracker.RecordRequest("curl/8.7.1", "GET");
        tracker.RecordRequest("Mac+OS+X/15.3 (24D60) CalendarAgent/1111", "PROPFIND");

        tracker.TotalRequests.Should().Be(3);
    }

    [Fact]
    public void TotalRequests_NoRequests_ReturnsZero()
    {
        var tracker = new ClientTracker();

        tracker.TotalRequests.Should().Be(0);
    }

    // ── Time window ──────────────────────────────────────────────────────────

    [Fact]
    public void GetActiveClients_ExpiredClients_Excluded()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("curl/8.7.1", "GET");

        // With a negative window the cutoff is in the future, so everything is expired.
        // We use -1ms instead of Zero to avoid a race where LastSeen == cutoff.
        var clients = tracker.GetActiveClients(TimeSpan.FromMilliseconds(-1));
        clients.Should().BeEmpty();
    }

    [Fact]
    public void GetActiveClients_OrderedByMostRecent()
    {
        var tracker = new ClientTracker();

        tracker.RecordRequest("Mac+OS+X/15.3 (24D60) CalendarAgent/1111", "PROPFIND");
        // Small delay to ensure ordering
        tracker.RecordRequest("curl/8.7.1", "GET");

        var clients = tracker.GetActiveClients(TimeSpan.FromHours(1));
        clients.Should().HaveCount(2);
        // Most recent (curl) should be first
        clients[0].DisplayName.Should().Be("curl");
    }
}
