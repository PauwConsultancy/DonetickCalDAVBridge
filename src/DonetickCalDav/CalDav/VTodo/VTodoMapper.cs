using DonetickCalDav.Donetick.Models;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

using CalendarProperty = Ical.Net.CalendarProperty;

namespace DonetickCalDav.CalDav.VTodo;

/// <summary>
/// Bidirectional mapper between Donetick chores and iCalendar VTODO components.
/// Uses Ical.Net for standards-compliant serialisation.
/// </summary>
public static class VTodoMapper
{
    private static readonly CalendarSerializer Serializer = new();

    /// <summary>
    /// Converts a Donetick chore to a complete VCALENDAR/VTODO ICS string.
    /// </summary>
    /// <param name="chore">The Donetick chore to convert.</param>
    /// <param name="allDayEvents">
    /// When true, emits DUE/DTSTART as VALUE=DATE (date only) so the task appears as an
    /// all-day item in Calendar.app and without a specific time in Reminders.app.
    /// </param>
    /// <param name="preserveScheduledTime">
    /// When true, replaces the time portion of NextDueDate with the originally configured
    /// time from FrequencyMetadata.Time (if available). Corrects for Donetick's behaviour
    /// of advancing NextDueDate using the completion time instead of the scheduled time.
    /// Has no visible effect when <paramref name="allDayEvents"/> is true.
    /// </param>
    public static string ToIcsString(DonetickChore chore, bool allDayEvents = false,
        bool preserveScheduledTime = false)
    {
        var todo = BuildTodo(chore, allDayEvents, preserveScheduledTime);
        var calendar = WrapInCalendar(todo);

        return Serializer.SerializeToString(calendar);
    }

    /// <summary>
    /// Converts an incoming VTODO (from Apple Reminders PUT) to a Donetick create request.
    /// Only name, description, and dueDate are supported by the eAPI.
    /// </summary>
    public static ChoreLiteRequest ToCreateRequest(Todo vtodo) => new()
    {
        Name = vtodo.Summary ?? "Untitled Task",
        Description = NullIfEmpty(vtodo.Description),
        DueDate = vtodo.Due?.AsDateTimeOffset.ToString("yyyy-MM-dd"),
    };

    /// <summary>
    /// Converts an incoming VTODO (from Apple Reminders PUT) to a Donetick update request.
    /// Only name, description, and dueDate are supported by the eAPI.
    /// </summary>
    public static ChoreLiteRequest ToUpdateRequest(Todo vtodo) => new()
    {
        Name = vtodo.Summary ?? "Untitled Task",
        Description = NullIfEmpty(vtodo.Description),
        DueDate = vtodo.Due?.AsDateTimeOffset.ToString("yyyy-MM-dd"),
    };

    /// <summary>Builds the core VTODO component from a Donetick chore.</summary>
    private static Todo BuildTodo(DonetickChore chore, bool allDayEvents, bool preserveScheduledTime)
    {
        var todo = new Todo
        {
            Uid = $"donetick-{chore.Id}@donetick",
            Summary = chore.Name ?? "Untitled",
            Description = chore.Description ?? "",
            DtStamp = new CalDateTime(chore.UpdatedAt, "UTC"),
            Created = new CalDateTime(chore.CreatedAt, "UTC"),
            LastModified = new CalDateTime(chore.UpdatedAt, "UTC"),
            Status = StatusMapper.ToVTodoStatus(chore.Status, chore.IsActive),
            Priority = PriorityMapper.ToVTodoPriority(chore.Priority),
        };

        ApplyDueDate(todo, chore, allDayEvents, preserveScheduledTime);
        // Note: we intentionally do NOT emit RRULE. Donetick manages recurrence
        // server-side (updating NextDueDate on completion). Emitting RRULE would
        // cause Calendar.app to generate occurrences client-side, conflicting
        // with the server-managed schedule.
        ApplyCategories(todo, chore);
        ApplyAppleHashtags(todo, chore);
        ApplyPercentComplete(todo, chore);

        return todo;
    }

    /// <summary>
    /// Sets the DUE and DTSTART properties on the VTODO.
    /// When <paramref name="allDayEvents"/> is true, emits VALUE=DATE (no time component).
    /// When <paramref name="preserveScheduledTime"/> is true and the chore has a configured
    /// time in FrequencyMetadata, the time portion of NextDueDate is replaced with that
    /// scheduled time (correcting for Donetick's completion-time-based advancement).
    /// </summary>
    private static void ApplyDueDate(Todo todo, DonetickChore chore, bool allDayEvents,
        bool preserveScheduledTime)
    {
        if (!chore.NextDueDate.HasValue) return;

        var effectiveDate = chore.NextDueDate.Value;

        // When enabled, replace the time portion with the originally configured scheduled time.
        // This corrects Donetick's behaviour of using completion time for NextDueDate advancement.
        if (preserveScheduledTime)
            effectiveDate = AdjustToScheduledTime(effectiveDate, chore.FrequencyMetadata);

        if (allDayEvents)
        {
            // VALUE=DATE — no time component, no timezone.
            // Calendar.app renders this as an all-day item at the top of the day.
            // Reminders.app shows "today" instead of "today at 14:00".
            // HasTime must be explicitly set to false so Ical.Net serialises as
            // "DUE;VALUE=DATE:YYYYMMDD" instead of "DUE:YYYYMMDDTHHMMSS".
            // IMPORTANT: separate CalDateTime instances — Ical.Net mutates objects
            // during serialisation, so sharing a single instance causes NullReferenceException.
            todo.Due = MakeDateOnly(effectiveDate);
            todo.DtStart = MakeDateOnly(effectiveDate);
        }
        else
        {
            todo.Due = new CalDateTime(effectiveDate, "UTC");
            // DtStart on the date portion aids Calendar.app display compatibility
            todo.DtStart = new CalDateTime(effectiveDate.Date, "UTC");
        }
    }

    /// <summary>
    /// Replaces the time portion of <paramref name="nextDueDate"/> with the configured
    /// scheduled time from <paramref name="metadata"/>, if available.
    /// <para>
    /// Handles timezone conversion: if <c>metadata.Timezone</c> is set, the date is converted
    /// to the local timezone, the time is replaced, and the result is converted back to UTC.
    /// This correctly handles DST transitions.
    /// </para>
    /// </summary>
    /// <returns>The adjusted date, or the original if no scheduled time is configured.</returns>
    internal static DateTime AdjustToScheduledTime(DateTime nextDueDate,
        DonetickFrequencyMetadata? metadata)
    {
        var timeStr = metadata?.Time;
        if (string.IsNullOrEmpty(timeStr)) return nextDueDate;
        if (!TimeOnly.TryParse(timeStr, out var scheduledTime)) return nextDueDate;

        var tzStr = metadata?.Timezone;

        if (!string.IsNullOrEmpty(tzStr))
        {
            try
            {
                var tz = TimeZoneInfo.FindSystemTimeZoneById(tzStr);

                // Convert UTC → local, replace time, convert back to UTC.
                // This ensures DST transitions are handled correctly.
                var utcDate = DateTime.SpecifyKind(nextDueDate, DateTimeKind.Utc);
                var local = TimeZoneInfo.ConvertTimeFromUtc(utcDate, tz);
                var adjusted = new DateTime(local.Year, local.Month, local.Day,
                    scheduledTime.Hour, scheduledTime.Minute, 0, DateTimeKind.Unspecified);
                return TimeZoneInfo.ConvertTimeToUtc(adjusted, tz);
            }
            catch (TimeZoneNotFoundException)
            {
                // Unknown timezone — fall through to UTC-based adjustment
            }
        }

        // No timezone or invalid timezone — adjust directly in UTC
        return new DateTime(nextDueDate.Year, nextDueDate.Month, nextDueDate.Day,
            scheduledTime.Hour, scheduledTime.Minute, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// Creates a date-only CalDateTime (VALUE=DATE) with no time component and no timezone.
    /// Uses explicit UTC DateTimeKind and null TzId to prevent Ical.Net's PropertySerializer
    /// from hitting null references on timezone lookups in containerised environments.
    /// </summary>
    private static CalDateTime MakeDateOnly(DateTime dt) =>
        new(new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Utc))
            { HasTime = false, TzId = null };

    private static void ApplyCategories(Todo todo, DonetickChore chore)
    {
        if (chore.LabelsV2 is not { Count: > 0 }) return;

        foreach (var label in chore.LabelsV2)
        {
            if (!string.IsNullOrEmpty(label.Name))
                todo.Categories.Add(label.Name);
        }
    }

    /// <summary>
    /// Emits X-APPLE-HASHTAGS so Apple Reminders / Calendar can display
    /// Donetick labels as native #hashtag-style tags. The standard CATEGORIES
    /// property is also emitted (by <see cref="ApplyCategories"/>) for
    /// non-Apple clients like Thunderbird and DAVx5.
    /// </summary>
    private static void ApplyAppleHashtags(Todo todo, DonetickChore chore)
    {
        if (chore.LabelsV2 is not { Count: > 0 }) return;

        var hashtags = string.Join(",", chore.LabelsV2
            .Where(l => !string.IsNullOrEmpty(l.Name))
            .Select(l => l.Name));
        if (string.IsNullOrEmpty(hashtags)) return;
        todo.Properties.Add(new CalendarProperty("X-APPLE-HASHTAGS", hashtags));
    }

    private static void ApplyPercentComplete(Todo todo, DonetickChore chore)
    {
        // Only set percent-complete for InProgress status as a visual hint
        if (chore.Status == 1)
            todo.PercentComplete = 50;
    }

    private static Calendar WrapInCalendar(Todo todo)
    {
        var calendar = new Calendar { ProductId = "-//Donetick//CalDAV Bridge//EN" };
        calendar.Todos.Add(todo);
        return calendar;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
