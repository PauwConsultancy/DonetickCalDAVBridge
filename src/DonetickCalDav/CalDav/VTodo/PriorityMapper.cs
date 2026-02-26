using DonetickCalDav.Donetick.Models;

namespace DonetickCalDav.CalDav.VTodo;

/// <summary>
/// Bidirectional mapping between Donetick priority (0-4) and
/// iCalendar PRIORITY (0-9, RFC 5545 Section 3.8.1.9).
/// </summary>
/// <remarks>
/// iCalendar: 0 = undefined, 1 = highest, 5 = medium, 9 = lowest.
/// </remarks>
public static class PriorityMapper
{
    /// <summary>Converts Donetick priority (0-4) to iCalendar PRIORITY (0-9).</summary>
    public static int ToVTodoPriority(int donetickPriority) => donetickPriority switch
    {
        ChorePriority.Low    => 9,  // Lowest iCal
        ChorePriority.Medium => 5,  // Medium iCal
        ChorePriority.High   => 3,  // High iCal
        ChorePriority.Urgent => 1,  // Highest iCal
        _ => 0,                     // Undefined iCal
    };

    /// <summary>Converts iCalendar PRIORITY (0-9) to Donetick priority (0-4).</summary>
    public static int FromVTodoPriority(int icalPriority) => icalPriority switch
    {
        0      => ChorePriority.None,    // Undefined
        1 or 2 => ChorePriority.Urgent,  // Highest
        3 or 4 => ChorePriority.High,    // High
        5 or 6 => ChorePriority.Medium,  // Medium
        >= 7   => ChorePriority.Low,     // Lowest
        _      => ChorePriority.None,
    };
}
