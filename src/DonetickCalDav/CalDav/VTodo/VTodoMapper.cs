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
    public static string ToIcsString(DonetickChore chore, bool allDayEvents = false)
    {
        var todo = BuildTodo(chore, allDayEvents);
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
    private static Todo BuildTodo(DonetickChore chore, bool allDayEvents)
    {
        var todo = new Todo
        {
            Uid = $"donetick-{chore.Id}@donetick",
            Summary = chore.Name,
            Description = chore.Description ?? "",
            DtStamp = new CalDateTime(chore.UpdatedAt, "UTC"),
            Created = new CalDateTime(chore.CreatedAt, "UTC"),
            LastModified = new CalDateTime(chore.UpdatedAt, "UTC"),
            Status = StatusMapper.ToVTodoStatus(chore.Status, chore.IsActive),
            Priority = PriorityMapper.ToVTodoPriority(chore.Priority),
        };

        ApplyDueDate(todo, chore, allDayEvents);
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
    /// When <paramref name="allDayEvents"/> is true, emits VALUE=DATE (no time component),
    /// causing the task to appear as an all-day item in Calendar.app and without a specific
    /// time in Reminders.app. Otherwise emits full DATE-TIME values in UTC.
    /// </summary>
    private static void ApplyDueDate(Todo todo, DonetickChore chore, bool allDayEvents)
    {
        if (!chore.NextDueDate.HasValue) return;

        if (allDayEvents)
        {
            // VALUE=DATE — no time component, no timezone.
            // Calendar.app renders this as an all-day item at the top of the day.
            // Reminders.app shows "today" instead of "today at 14:00".
            // HasTime must be explicitly set to false so Ical.Net serialises as
            // "DUE;VALUE=DATE:YYYYMMDD" instead of "DUE:YYYYMMDDTHHMMSS".
            var d = chore.NextDueDate.Value;
            var dateOnly = new CalDateTime(d.Year, d.Month, d.Day) { HasTime = false };
            todo.Due = dateOnly;
            todo.DtStart = dateOnly;
        }
        else
        {
            todo.Due = new CalDateTime(chore.NextDueDate.Value, "UTC");
            // DtStart on the date portion aids Calendar.app display compatibility
            todo.DtStart = new CalDateTime(chore.NextDueDate.Value.Date, "UTC");
        }
    }

    private static void ApplyCategories(Todo todo, DonetickChore chore)
    {
        if (chore.LabelsV2 is not { Count: > 0 }) return;

        foreach (var label in chore.LabelsV2)
            todo.Categories.Add(label.Name);
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

        var hashtags = string.Join(",", chore.LabelsV2.Select(l => l.Name));
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
