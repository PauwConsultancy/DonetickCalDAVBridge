namespace DonetickCalDav.CalDav.VTodo;

/// <summary>
/// Bidirectional mapping between Donetick priority (0-4) and
/// iCalendar PRIORITY (0-9, RFC 5545 Section 3.8.1.9).
/// </summary>
/// <remarks>
/// iCalendar: 0 = undefined, 1 = highest, 5 = medium, 9 = lowest.
/// Donetick:  0 = none, 1 = low, 2 = medium, 3 = high, 4 = urgent.
/// </remarks>
public static class PriorityMapper
{
    /// <summary>Converts Donetick priority (0-4) to iCalendar PRIORITY (0-9).</summary>
    public static int ToVTodoPriority(int donetickPriority) => donetickPriority switch
    {
        1 => 9,  // Low    → lowest iCal
        2 => 5,  // Medium → medium iCal
        3 => 3,  // High   → high iCal
        4 => 1,  // Urgent → highest iCal
        _ => 0,  // None   → undefined iCal
    };

    /// <summary>Converts iCalendar PRIORITY (0-9) to Donetick priority (0-4).</summary>
    public static int FromVTodoPriority(int icalPriority) => icalPriority switch
    {
        0      => 0,  // Undefined → None
        1 or 2 => 4,  // Highest   → Urgent
        3 or 4 => 3,  // High      → High
        5 or 6 => 2,  // Medium    → Medium
        >= 7   => 1,  // Lowest    → Low
        _      => 0,
    };
}
