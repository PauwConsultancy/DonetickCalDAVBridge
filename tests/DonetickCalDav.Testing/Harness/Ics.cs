using System.Globalization;
using System.Text;

namespace DonetickCalDav.Testing.Harness;

/// <summary>
/// Builds VTODO bodies the way Apple Reminders sends them in a PUT: CRLF line endings, a PRODID
/// of the client, DTSTAMP, and DUE as a date or a UTC date-time.
/// </summary>
public static class Ics
{
    public const string AppleProdId = "-//Apple Inc.//macOS 26.0//EN";

    public static string VTodo(
        string uid,
        string summary,
        string status = "NEEDS-ACTION",
        string? description = null,
        DateOnly? dueDate = null,
        DateTime? dueDateTimeUtc = null,
        string prodId = AppleProdId)
    {
        var sb = new StringBuilder();
        void Line(string line) => sb.Append(line).Append("\r\n");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line($"PRODID:{prodId}");
        Line("BEGIN:VTODO");
        Line($"UID:{uid}");
        Line("DTSTAMP:20250615T080000Z");
        Line($"SUMMARY:{Escape(summary)}");
        if (description != null) Line($"DESCRIPTION:{Escape(description)}");
        if (dueDate is { } date) Line($"DUE;VALUE=DATE:{date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}");
        if (dueDateTimeUtc is { } dateTime)
            Line($"DUE:{dateTime.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}");
        Line($"STATUS:{status}");
        if (status == "COMPLETED") Line("COMPLETED:20250615T080000Z");
        Line("END:VTODO");
        Line("END:VCALENDAR");
        return sb.ToString();
    }

    /// <summary>Path of the canonical resource for a Donetick chore in a calendar.</summary>
    public static string ResourcePath(string user, int choreId, string calendar = "tasks") =>
        $"/caldav/calendars/{user}/{calendar}/donetick-{choreId}.ics";

    /// <summary>UID the bridge generates for a Donetick chore.</summary>
    public static string ChoreUid(int choreId) => $"donetick-{choreId}@donetick";

    private static string Escape(string text) =>
        text.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
