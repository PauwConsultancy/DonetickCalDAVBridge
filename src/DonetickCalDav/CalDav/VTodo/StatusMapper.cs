using DonetickCalDav.Donetick.Models;

namespace DonetickCalDav.CalDav.VTodo;

/// <summary>
/// Bidirectional mapping between Donetick chore status integers
/// and iCalendar VTODO STATUS strings (RFC 5545 Section 3.8.1.11).
/// </summary>
/// <remarks>
/// VTODO statuses: NEEDS-ACTION, IN-PROCESS, COMPLETED, CANCELLED.
/// Note: iCalendar has no "Paused" concept; we map it to NEEDS-ACTION.
/// </remarks>
public static class StatusMapper
{
    /// <summary>Converts a Donetick status + active flag to an iCalendar VTODO STATUS string.</summary>
    public static string ToVTodoStatus(int donetickStatus, bool isActive)
    {
        if (!isActive) return "CANCELLED";

        return donetickStatus switch
        {
            ChoreStatus.InProgress       => "IN-PROCESS",
            ChoreStatus.PendingApproval  => "IN-PROCESS",     // Closest match
            _ => "NEEDS-ACTION",   // NoStatus and Paused
        };
    }

    /// <summary>
    /// Converts an iCalendar VTODO STATUS string back to a Donetick action.
    /// Returns the target status integer and whether the chore should be completed.
    /// </summary>
    public static (int? Status, bool ShouldComplete) FromVTodoStatus(string? vtodoStatus)
    {
        return vtodoStatus?.ToUpperInvariant() switch
        {
            "COMPLETED"    => (null, true),
            "IN-PROCESS"   => (ChoreStatus.InProgress, false),
            "CANCELLED"    => (null, false),
            "NEEDS-ACTION" => (ChoreStatus.NoStatus, false),
            _              => (ChoreStatus.NoStatus, false),
        };
    }
}
