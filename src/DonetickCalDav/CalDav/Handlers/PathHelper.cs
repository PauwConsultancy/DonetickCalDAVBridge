using System.Text.RegularExpressions;
using DonetickCalDav.Cache;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Utility for extracting Donetick chore IDs from CalDAV resource paths.
/// Supports both our canonical "donetick-{id}.ics" paths and Apple-generated UUID paths.
/// </summary>
public static partial class PathHelper
{
    /// <summary>
    /// Extracts the numeric chore ID from a CalDAV resource path like
    /// "/caldav/calendars/{user}/tasks/donetick-{id}.ics".
    /// Returns null if the path does not match the expected pattern.
    /// </summary>
    public static int? ExtractChoreId(string path)
    {
        var match = ChoreIdPattern().Match(path);
        if (!match.Success) return null;

        return int.TryParse(match.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>
    /// Extracts the UUID filename from an Apple-generated CalDAV resource path like
    /// "/caldav/calendars/{user}/tasks/{UUID}.ics".
    /// Returns the UUID part without .ics extension, or null if no match.
    /// </summary>
    public static string? ExtractUuidFilename(string path)
    {
        var match = UuidFilenamePattern().Match(path);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Resolves a chore ID from a CalDAV path using all available lookup strategies:
    ///   1. Our canonical path pattern (donetick-{id}.ics)
    ///   2. Apple UUID filename → UID-to-ID cache map
    /// Use this for GET/DELETE handlers that only have a path (no VTODO body).
    /// </summary>
    public static int? ResolveChoreIdFromPath(string path, ChoreCache cache)
    {
        // 1. Our canonical format: donetick-{id}.ics
        var id = ExtractChoreId(path);
        if (id.HasValue) return id;

        // 2. Apple UUID filename → try cache UID-to-ID map
        var uuid = ExtractUuidFilename(path);
        return uuid != null ? cache.GetIdByUid(uuid) : null;
    }

    /// <summary>
    /// Extracts the calendar collection slug from a CalDAV path.
    /// "/caldav/calendars/user/werk/donetick-1.ics" → "werk"
    /// "/caldav/calendars/user/tasks/" → "tasks"
    /// "/caldav/principals/user/" → null (not a calendar path)
    /// </summary>
    public static string? ExtractCalendarSlug(string path, string username)
    {
        var match = CalendarSlugPattern(username).Match(path);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }

    [GeneratedRegex(@"donetick-(\d+)\.ics$", RegexOptions.Compiled)]
    private static partial Regex ChoreIdPattern();

    [GeneratedRegex(@"/([0-9A-Fa-f-]{36})\.ics$", RegexOptions.Compiled)]
    private static partial Regex UuidFilenamePattern();

    /// <summary>
    /// Builds a regex to extract the calendar slug from a CalDAV path.
    /// The slug is the path segment immediately after /caldav/calendars/{username}/.
    /// </summary>
    private static Regex CalendarSlugPattern(string username) =>
        new($@"/caldav/calendars/{Regex.Escape(username)}/([^/]+)", RegexOptions.IgnoreCase);
}
