using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Resources;

/// <summary>IT-05: fetching single to-dos with GET and HEAD.</summary>
[Trait("Scenario", "IT-05")]
[Trait("Covers", "UC-02")]
[Trait("Covers", "FR-10")]
public class GetTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    public async Task Get_KnownChore_ReturnsIcsWithETag()
    {
        Donetick.WithChores(TestChoreFactory.WithDueDate(2));
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();

        var response = await client.GetAsync(bridge.ResourcePath(2));

        response.Status.Should().Be(HttpStatusCode.OK);
        response.Header("Content-Type").Should().Be("text/calendar; charset=utf-8");
        response.ETag.Should().MatchRegex("^\"[0-9A-F]{16}\"$");
        response.Body.Should().StartWith("BEGIN:VCALENDAR");
        Ics.Value(response.Body, "UID").Should().Be(Ics.ChoreUid(2));
        Ics.Line(response.Body, "DUE").Should().Be("DUE:20250618T100000Z");

        var propfind = await client.PropFindAsync(bridge.ResourcePath(2), 0, Dav.D + "getetag");
        propfind.GetETag(bridge.ResourcePath(2)).Should().Be(response.ETag);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Get_MatchingIfNoneMatch_Returns304AndOtherETagReturnsTheBody()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var etag = (await client.GetAsync(bridge.ResourcePath(1))).ETag!;

        var notModified = await client.GetAsync(bridge.ResourcePath(1), ifNoneMatch: etag);
        var modified = await client.GetAsync(bridge.ResourcePath(1), ifNoneMatch: "\"0000000000000000\"");

        notModified.Status.Should().Be(HttpStatusCode.NotModified);
        notModified.ETag.Should().Be(etag);
        notModified.Body.Should().BeEmpty();
        modified.Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Head_KnownChore_ReturnsETag()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().HeadAsync(bridge.ResourcePath(1));

        response.Status.Should().Be(HttpStatusCode.OK);
        response.ETag.Should().NotBeNullOrEmpty();
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("donetick-999.ics")]
    [InlineData("3F2504E0-4F89-11D3-9A0C-0305E82C3301.ics")]
    [InlineData("notes.txt")]
    public async Task Get_UnknownResource_Returns404(string fileName)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().GetAsync(bridge.CalendarPath() + fileName);

        response.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Get_AfterDonetickChangedTheChore_ServesTheNewVersionAfterThePoll()
    {
        var chore = TestChoreFactory.Simple(1, "Before");
        Donetick.WithChores(chore);
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var before = await client.GetAsync(bridge.ResourcePath(1));

        var changed = TestChoreFactory.Simple(1, "After");
        changed.UpdatedAt = chore.UpdatedAt.AddMinutes(1);
        Donetick.WithChores(changed);
        await bridge.RunPollAsync();
        var after = await client.GetAsync(bridge.ResourcePath(1));

        Ics.Value(after.Body, "SUMMARY").Should().Be("After");
        after.ETag.Should().NotBe(before.ETag);
    }
}
