using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Protocol;

/// <summary>IT-10: operations the bridge rejects (moving between lists, creating lists, changing list properties).</summary>
[Trait("Scenario", "IT-10")]
[Trait("Covers", "UC-08")]
[Trait("Covers", "UC-09")]
[Trait("Covers", "FR-17")]
[Trait("Covers", "FR-18")]
public class UnsupportedOperationTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    private const string AllowedMethods = "OPTIONS, GET, HEAD, PUT, DELETE, PROPFIND, PROPPATCH, REPORT";

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("MOVE")]
    [InlineData("MKCALENDAR")]
    [InlineData("MKCOL")]
    public async Task Method_NotSupportedByDonetick_Returns403WithAllow(string method)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var requestsBefore = Donetick.AllRequests.Count;

        var response = await bridge.CreateClient().SendAsync(new HttpMethod(method), bridge.ResourcePath(1),
            configure: r => r.Headers.TryAddWithoutValidation("Destination", bridge.ResourcePath(1, "home")));

        response.Status.Should().Be(HttpStatusCode.Forbidden);
        response.Header("Allow").Should().Be(AllowedMethods);
        Donetick.AllRequests.Should().HaveCount(requestsBefore);
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("POST")]
    [InlineData("PATCH")]
    [InlineData("COPY")]
    [InlineData("LOCK")]
    public async Task Method_Unknown_Returns405WithAllow(string method)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().SendAsync(new HttpMethod(method), bridge.ResourcePath(1));

        response.Status.Should().Be(HttpStatusCode.MethodNotAllowed);
        response.Header("Allow").Should().Be(AllowedMethods);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task PropPatch_ListColour_IsRejectedPerPropertyWith404()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var calendar = bridge.CalendarPath();

        var response = await bridge.CreateClient().SendAsync(new HttpMethod("PROPPATCH"), calendar,
            Dav.PropPatch((Dav.Apple + "calendar-color", "#FF0000FF"), (Dav.D + "displayname", "Renamed")));

        response.Status.Should().Be((HttpStatusCode)207);
        response.Hrefs.Should().Equal(calendar);
        response.IsNotFound(calendar, Dav.Apple + "calendar-color").Should().BeTrue();
        response.IsNotFound(calendar, Dav.D + "displayname").Should().BeTrue();

        var after = await bridge.CreateClient().PropFindAsync(calendar, 0, Dav.Apple + "calendar-color");
        after.Property(calendar, Dav.Apple + "calendar-color")!.Value.Should().Be("#4A90D9FF", "nothing is stored");
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task PropPatch_EmptyBody_Returns207()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().SendAsync(new HttpMethod("PROPPATCH"), bridge.CalendarPath());

        response.Status.Should().Be((HttpStatusCode)207);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Request_OutsideCalDavAndStatusPaths_Returns404()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().GetAsync("/api/v1/chores");

        response.Status.Should().Be(HttpStatusCode.NotFound);
    }
}
