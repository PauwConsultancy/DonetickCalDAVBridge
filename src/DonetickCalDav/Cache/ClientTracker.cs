using System.Collections.Concurrent;

namespace DonetickCalDav.Cache;

/// <summary>
/// Thread-safe tracker for CalDAV client activity.
/// Records which clients are connecting and when they were last seen.
/// </summary>
public sealed class ClientTracker
{
    private readonly ConcurrentDictionary<string, ClientInfo> _clients = new();

    /// <summary>
    /// Records an authenticated CalDAV request from a client.
    /// </summary>
    public void RecordRequest(string? userAgent, string method)
    {
        var key = NormalizeClientKey(userAgent);
        var displayName = ParseDisplayName(userAgent);

        _clients.AddOrUpdate(
            key,
            _ => new ClientInfo(displayName, userAgent?.Truncate(200), DateTime.UtcNow, 1),
            (_, existing) => existing with
            {
                LastSeen = DateTime.UtcNow,
                RequestCount = existing.RequestCount + 1,
            });
    }

    /// <summary>
    /// Returns all clients seen within the given time window, ordered by most recent.
    /// </summary>
    public List<ClientSnapshot> GetActiveClients(TimeSpan window)
    {
        var cutoff = DateTime.UtcNow - window;

        return _clients.Values
            .Where(c => c.LastSeen >= cutoff)
            .OrderByDescending(c => c.LastSeen)
            .Select(c => new ClientSnapshot(c.DisplayName, c.RawUserAgent, c.LastSeen, c.RequestCount))
            .ToList();
    }

    /// <summary>
    /// Total number of authenticated CalDAV requests since startup.
    /// </summary>
    public long TotalRequests => _clients.Values.Sum(c => c.RequestCount);

    /// <summary>
    /// Normalizes the User-Agent to a stable key for grouping.
    /// Different versions of the same client should map to one key.
    /// </summary>
    private static string NormalizeClientKey(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return "unknown";

        // Apple CalendarAgent: "Mac+OS+X/15.3 (24D60) CalendarAgent/1111"
        if (userAgent.Contains("CalendarAgent", StringComparison.OrdinalIgnoreCase))
            return "apple-calendar-mac";

        // Apple Reminders daemon: "Mac+OS+X/15.3 (24D60) remindd/1111"
        if (userAgent.Contains("remindd", StringComparison.OrdinalIgnoreCase))
            return "apple-reminders-mac";

        // iOS CalDAV: "iOS/18.3 (22D60) dataaccessd/1.0"
        if (userAgent.Contains("dataaccessd", StringComparison.OrdinalIgnoreCase))
            return "apple-ios";

        // iOS AccountsManagerAgent
        if (userAgent.Contains("AccountsManagerAgent", StringComparison.OrdinalIgnoreCase))
            return "apple-ios-accounts";

        // Thunderbird
        if (userAgent.Contains("Thunderbird", StringComparison.OrdinalIgnoreCase))
            return "thunderbird";

        // GNOME / Evolution
        if (userAgent.Contains("Evolution", StringComparison.OrdinalIgnoreCase))
            return "gnome-evolution";

        // DAVx5 (Android)
        if (userAgent.Contains("DAVx5", StringComparison.OrdinalIgnoreCase))
            return "davx5-android";

        // curl / wget / httpie (testing)
        if (userAgent.StartsWith("curl/", StringComparison.OrdinalIgnoreCase))
            return "curl";
        if (userAgent.StartsWith("Wget/", StringComparison.OrdinalIgnoreCase))
            return "wget";

        return $"other-{userAgent.GetHashCode():X8}";
    }

    /// <summary>
    /// Extracts a human-friendly display name from the User-Agent string.
    /// </summary>
    private static string ParseDisplayName(string? userAgent)
    {
        if (string.IsNullOrEmpty(userAgent))
            return "Unknown client";

        if (userAgent.Contains("CalendarAgent", StringComparison.OrdinalIgnoreCase))
            return "Apple Calendar (macOS)";

        if (userAgent.Contains("remindd", StringComparison.OrdinalIgnoreCase))
            return "Apple Reminders (macOS)";

        if (userAgent.Contains("dataaccessd", StringComparison.OrdinalIgnoreCase))
            return "Apple Calendar/Reminders (iOS)";

        if (userAgent.Contains("AccountsManagerAgent", StringComparison.OrdinalIgnoreCase))
            return "Apple Accounts (iOS)";

        if (userAgent.Contains("Thunderbird", StringComparison.OrdinalIgnoreCase))
            return "Mozilla Thunderbird";

        if (userAgent.Contains("Evolution", StringComparison.OrdinalIgnoreCase))
            return "GNOME Evolution";

        if (userAgent.Contains("DAVx5", StringComparison.OrdinalIgnoreCase))
            return "DAVx5 (Android)";

        if (userAgent.StartsWith("curl/", StringComparison.OrdinalIgnoreCase))
            return "curl";

        if (userAgent.StartsWith("Wget/", StringComparison.OrdinalIgnoreCase))
            return "wget";

        // Truncate unknown User-Agents for display
        return userAgent.Length > 40 ? userAgent[..37] + "..." : userAgent;
    }

    private sealed record ClientInfo(
        string DisplayName,
        string? RawUserAgent,
        DateTime LastSeen,
        long RequestCount);
}

/// <summary>
/// Immutable snapshot of a tracked client for display purposes.
/// </summary>
public sealed record ClientSnapshot(
    string DisplayName,
    string? RawUserAgent,
    DateTime LastSeen,
    long RequestCount);

/// <summary>String extension helpers.</summary>
internal static class StringExtensions
{
    public static string Truncate(this string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 3)] + "...";
}
