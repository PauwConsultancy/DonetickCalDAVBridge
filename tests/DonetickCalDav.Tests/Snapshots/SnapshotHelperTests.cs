using DonetickCalDav.Testing.Snapshots;
using FluentAssertions;

namespace DonetickCalDav.Tests.Snapshots;

/// <summary>The in-house snapshot helper (task P2-01): new, match, mismatch, accept and CI cases.</summary>
public sealed class SnapshotHelperTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "snapshot-tests-" + Guid.NewGuid().ToString("N"));
    private readonly SnapshotFile _file;
    private readonly Dictionary<string, string?> _environment = new();

    public SnapshotHelperTests()
    {
        _file = new SnapshotFile(_directory, "Example.Test", "ics");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Verify_NoApprovedFile_FailsAndWritesReceived()
    {
        var act = () => Verify("SUMMARY:New\n");

        act.Should().Throw<SnapshotMismatchException>().WithMessage("New snapshot, review *Example.Test.received.ics*");
        File.ReadAllText(_file.ReceivedPath).Should().Be("SUMMARY:New\n");
        File.Exists(_file.VerifiedPath).Should().BeFalse();
    }

    [Fact]
    public void Verify_MatchingApprovedFile_PassesAndRemovesAStaleReceivedFile()
    {
        Approve("SUMMARY:Same\n");
        File.WriteAllText(_file.ReceivedPath, "old");

        Verify("SUMMARY:Same\n");

        File.Exists(_file.ReceivedPath).Should().BeFalse();
    }

    [Fact]
    public void Verify_DifferentLineEndingsAndTrailingNewlines_StillMatch()
    {
        Approve("A\nB\n");

        var act = () => Verify("A\r\nB\r\n\r\n");

        act.Should().NotThrow();
    }

    [Fact]
    public void Verify_Mismatch_FailsWithALineDiff()
    {
        Approve("BEGIN:VTODO\nSTATUS:NEEDS-ACTION\nSUMMARY:Shave\nUID:donetick-42@donetick\nEND:VTODO\n");

        var act = () => Verify("BEGIN:VTODO\nSTATUS:NEEDS-ACTION\nSUMMARY:Shave daily\nUID:donetick-42@donetick\nEND:VTODO\n");

        var message = act.Should().Throw<SnapshotMismatchException>().Which.Message;
        message.Should().Contain("Snapshot mismatch for Example.Test.verified.ics");
        message.Should().Contain("- SUMMARY:Shave\n").And.Contain("+ SUMMARY:Shave daily\n");
        message.Should().Contain("  STATUS:NEEDS-ACTION\n").And.Contain("  UID:donetick-42@donetick\n");
        message.Should().NotContain("BEGIN:VTODO", "only one line of context is shown");
        File.Exists(_file.ReceivedPath).Should().BeTrue();
    }

    [Fact]
    public void Verify_AcceptVariableSet_ApprovesTheNewOutput()
    {
        Approve("SUMMARY:Old\n");
        _environment[Snapshot.AcceptVariable] = "1";

        Verify("SUMMARY:New\n");

        File.ReadAllText(_file.VerifiedPath).Should().Be("SUMMARY:New\n");
        File.Exists(_file.ReceivedPath).Should().BeFalse();
    }

    [Fact]
    public void Verify_AcceptVariableSetOnCi_StillFails()
    {
        Approve("SUMMARY:Old\n");
        _environment[Snapshot.AcceptVariable] = "1";
        _environment["CI"] = "true";

        var act = () => Verify("SUMMARY:New\n");

        act.Should().Throw<SnapshotMismatchException>();
        File.ReadAllText(_file.VerifiedPath).Should().Be("SUMMARY:Old\n");
    }

    [Fact]
    public void Verify_Scrubbers_RunBeforeComparing()
    {
        Approve("ctag \"{ctag}\" and {guid}\n");

        var act = () => Snapshot.Verify("ctag \"0123456789abcdef0123456789abcdef\" and 3F2504E0-4F89-11D3-9A0C-0305E82C3301\n",
            _file, [Scrubbers.CTag, Scrubbers.Guids], name => _environment.GetValueOrDefault(name));

        act.Should().NotThrow();
    }

    [Fact]
    public void Match_DefaultLocation_IsASnapshotsFolderNextToTheTestSource()
    {
        var file = SnapshotFile.For("/repo/tests/X/VTodo/MapperTests.cs", "Ics_Simple", "allday", "ics");

        file.VerifiedPath.Should().Be(Path.Combine("/repo/tests/X/VTodo/Snapshots", "MapperTests.Ics_Simple.allday.verified.ics"));
        file.ReceivedPath.Should().EndWith("MapperTests.Ics_Simple.allday.received.ics");
    }

    [Fact]
    public void LineDiff_DistantChanges_AreSeparatedByAnEllipsis()
    {
        var diff = Snapshot.LineDiff("1\n2\n3\n4\n5\n6\n7\n", "x\n2\n3\n4\n5\n6\ny\n");

        diff.Should().Be("- 1\n+ x\n  2\n  ...\n  6\n- 7\n+ y\n");
    }

    [Fact]
    public void Xml_ReindentsWithoutChangingOrder()
    {
        var xml = Scrubbers.Xml("<?xml version=\"1.0\" encoding=\"utf-8\"?><a><c>1</c><b /></a>");

        xml.Should().Be("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<a>\n  <c>1</c>\n  <b />\n</a>");
    }

    [Fact]
    public void Scrubbers_UptimeVersionAndPorts_AreReplaced()
    {
        var text = $"\"uptime_seconds\": 42, \"version\": \"{Scrubbers.ApplicationVersion}\", \"base_url\": \"http://localhost:41211\"";

        var scrubbed = Scrubbers.Http.Aggregate(text, (t, scrub) => scrub(t));

        scrubbed.Should().Be("\"uptime_seconds\": 0, \"version\": \"{version}\", \"base_url\": \"http://localhost:{port}\"");
    }

    [Fact]
    public void Scrubbers_Version_AlsoReplacesTheJsonEscapedForm()
    {
        var escaped = Scrubbers.ApplicationVersion.Replace("+", "\\u002B");

        Scrubbers.Version($"\"version\": \"{escaped}\"").Should().Be("\"version\": \"{version}\"");
    }

    private void Approve(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_file.VerifiedPath, content);
    }

    private void Verify(string actual) =>
        Snapshot.Verify(actual, _file, null, name => _environment.GetValueOrDefault(name));
}
