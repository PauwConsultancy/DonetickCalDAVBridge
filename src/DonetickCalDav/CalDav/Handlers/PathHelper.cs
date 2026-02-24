using System.Text.RegularExpressions;

namespace DonetickCalDav.CalDav.Handlers;

/// <summary>
/// Utility for extracting Donetick chore IDs from CalDAV resource paths.
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

    [GeneratedRegex(@"donetick-(\d+)\.ics$", RegexOptions.Compiled)]
    private static partial Regex ChoreIdPattern();
}
