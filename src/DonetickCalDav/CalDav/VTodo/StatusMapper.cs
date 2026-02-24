namespace DonetickCalDav.CalDav.VTodo;

/// <summary>
/// Bidirectional mapping between Donetick chore status integers
/// and iCalendar VTODO STATUS strings (RFC 5545 Section 3.8.1.11).
/// </summary>
/// <remarks>
/// Donetick statuses: 0 = NoStatus, 1 = InProgress, 2 = Paused, 3 = PendingApproval.
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
            1 => "IN-PROCESS",     // InProgress
            3 => "IN-PROCESS",     // PendingApproval — closest match
            _ => "NEEDS-ACTION",   // NoStatus (0) and Paused (2)
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
            "COMPLETED"    => (null, true),   // Trigger the /complete endpoint
            "IN-PROCESS"   => (1, false),     // InProgress
            "CANCELLED"    => (null, false),  // Deactivate (if API supports it)
            "NEEDS-ACTION" => (0, false),     // NoStatus
            _              => (0, false),
        };
    }
}
