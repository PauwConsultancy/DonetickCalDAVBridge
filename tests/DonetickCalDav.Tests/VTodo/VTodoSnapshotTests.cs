using DonetickCalDav.CalDav.VTodo;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Snapshots;

namespace DonetickCalDav.Tests.VTodo;

/// <summary>
/// SN-01: the ICS the bridge serves, for every chore scenario and every combination of AllDayEvents and
/// PreserveScheduledTime. Any change to the VTODO shape fails here and has to be approved explicitly
/// (instructions.md: do not change the VTODO shape unintentionally).
/// </summary>
[Trait("Level", "Snapshot")]
[Trait("Scenario", "SN-01")]
public class VTodoSnapshotTests
{
    public static TheoryData<string, bool, bool> Matrix()
    {
        var data = new TheoryData<string, bool, bool>();
        foreach (var name in ChoreMatrix.Names)
            foreach (var allDay in new[] { false, true })
                foreach (var preserve in new[] { false, true })
                    data.Add(name, allDay, preserve);
        return data;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void ToIcsString_ChoreMatrix_MatchesApprovedIcs(string scenario, bool allDayEvents, bool preserveScheduledTime)
    {
        var chore = ChoreMatrix.Scenarios[scenario]();

        var ics = VTodoMapper.ToIcsString(chore, allDayEvents, preserveScheduledTime);

        Snapshot.Match(ics, "ics",
            discriminator: $"{scenario}.{(allDayEvents ? "allday" : "timed")}.{(preserveScheduledTime ? "preserve" : "as-is")}");
    }
}
