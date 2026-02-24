using DonetickCalDav.Donetick.Models;
using Ical.Net;
using Ical.Net.DataTypes;

namespace DonetickCalDav.CalDav.VTodo;

/// <summary>
/// Maps Donetick frequency configuration (frequencyType + frequency + frequencyMetadata)
/// to iCalendar RRULE RecurrencePatterns.
/// </summary>
/// <remarks>
/// Not all Donetick frequency types can be represented as RRULE:
/// - "adaptive" uses ML-based intervals → no static RRULE equivalent.
/// - "trigger"  is externally triggered  → no time-based recurrence.
/// - "once" / "no_repeat"               → simply omit the RRULE.
/// </remarks>
public static class RecurrenceMapper
{
    private static readonly Dictionary<string, DayOfWeek> DayNameMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["monday"] = DayOfWeek.Monday,       ["mon"] = DayOfWeek.Monday,
        ["tuesday"] = DayOfWeek.Tuesday,     ["tue"] = DayOfWeek.Tuesday,
        ["wednesday"] = DayOfWeek.Wednesday, ["wed"] = DayOfWeek.Wednesday,
        ["thursday"] = DayOfWeek.Thursday,   ["thu"] = DayOfWeek.Thursday,
        ["friday"] = DayOfWeek.Friday,       ["fri"] = DayOfWeek.Friday,
        ["saturday"] = DayOfWeek.Saturday,   ["sat"] = DayOfWeek.Saturday,
        ["sunday"] = DayOfWeek.Sunday,       ["sun"] = DayOfWeek.Sunday,
    };

    /// <summary>
    /// Converts a Donetick chore's frequency settings to an iCalendar RecurrencePattern.
    /// Returns null for non-recurring or non-representable frequency types.
    /// </summary>
    public static RecurrencePattern? ToRRule(DonetickChore chore)
    {
        var interval = Math.Max(1, chore.Frequency);

        return chore.FrequencyType?.ToLowerInvariant() switch
        {
            "daily"            => SimplePattern(FrequencyType.Daily, interval),
            "weekly"           => SimplePattern(FrequencyType.Weekly, interval),
            "monthly"          => SimplePattern(FrequencyType.Monthly, interval),
            "yearly"           => SimplePattern(FrequencyType.Yearly, interval),
            "interval"         => BuildIntervalRule(chore),
            "days_of_the_week" => BuildDaysOfWeekRule(chore),
            "day_of_the_month" => BuildDayOfMonthRule(chore),
            _                  => null, // once, no_repeat, trigger, adaptive
        };
    }

    private static RecurrencePattern SimplePattern(FrequencyType freq, int interval) =>
        new(freq) { Interval = interval };

    /// <summary>
    /// Builds a rule for the "interval" frequency type, where the metadata unit
    /// determines whether we recur hourly, daily, weekly, or monthly.
    /// </summary>
    private static RecurrencePattern BuildIntervalRule(DonetickChore chore)
    {
        var unit = chore.FrequencyMetadata?.Unit?.ToLowerInvariant();
        var interval = Math.Max(1, chore.Frequency);

        var freq = unit switch
        {
            "hours"  => FrequencyType.Hourly,
            "days"   => FrequencyType.Daily,
            "weeks"  => FrequencyType.Weekly,
            "months" => FrequencyType.Monthly,
            _        => FrequencyType.Daily,
        };

        return new RecurrencePattern(freq) { Interval = interval };
    }

    /// <summary>
    /// Builds a WEEKLY rule with BYDAY from metadata.days (e.g. ["monday", "wednesday", "friday"]).
    /// </summary>
    private static RecurrencePattern? BuildDaysOfWeekRule(DonetickChore chore)
    {
        var days = chore.FrequencyMetadata?.Days;
        if (days is not { Count: > 0 }) return null;

        var pattern = new RecurrencePattern(FrequencyType.Weekly) { Interval = 1 };

        foreach (var day in days)
        {
            if (day == null) continue;
            if (!DayNameMap.TryGetValue(day, out var dow)) continue;

            pattern.ByDay.Add(new WeekDay(dow));
        }

        return pattern.ByDay.Count > 0 ? pattern : null;
    }

    /// <summary>
    /// Builds a MONTHLY rule with BYMONTHDAY from metadata.days (e.g. ["1", "15"]).
    /// </summary>
    private static RecurrencePattern? BuildDayOfMonthRule(DonetickChore chore)
    {
        var days = chore.FrequencyMetadata?.Days;
        if (days is not { Count: > 0 }) return null;

        var pattern = new RecurrencePattern(FrequencyType.Monthly)
        {
            Interval = Math.Max(1, chore.Frequency)
        };

        foreach (var day in days)
        {
            if (day == null) continue;
            if (!int.TryParse(day, out var dayNum)) continue;

            pattern.ByMonthDay.Add(dayNum);
        }

        return pattern.ByMonthDay.Count > 0 ? pattern : null;
    }
}
