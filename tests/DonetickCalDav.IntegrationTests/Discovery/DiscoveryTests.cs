using System.Net;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Discovery;

/// <summary>IT-01: the discovery chain as Apple Calendar and Reminders perform it when an account is added.</summary>
[Trait("Covers", "UC-01")]
public class DiscoveryTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    [Fact(Timeout = DefaultTimeout)]
    [Trait("Scenario", "IT-01")]
    public async Task Discovery_AppleAccountSetup_LeadsFromWellKnownToTheTaskCalendar()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1), TestChoreFactory.WithDueDate(2));
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient();
        var anonymous = client.Anonymous();

        // 1. .well-known redirects to the CalDAV root, without authentication, and already says DAV.
        var wellKnown = await anonymous.PropFindAsync("/.well-known/caldav", 0);
        wellKnown.Status.Should().Be(HttpStatusCode.MovedPermanently);
        wellKnown.Header("Location").Should().Be("/caldav/");
        wellKnown.Header("DAV").Should().Contain("calendar-access");

        // 2. OPTIONS works without authentication and announces the capabilities.
        var options = await anonymous.OptionsAsync("/caldav/");
        options.Status.Should().Be(HttpStatusCode.OK);
        options.Header("DAV").Should().Contain("calendar-access");
        options.Header("Allow").Should().Contain("PROPFIND").And.Contain("REPORT");

        // 3. The first PROPFIND without credentials gets a Basic challenge that still carries DAV.
        var challenge = await anonymous.PropFindAsync("/caldav/", 0, Dav.D + "current-user-principal");
        challenge.Status.Should().Be(HttpStatusCode.Unauthorized);
        challenge.Header("WWW-Authenticate").Should().StartWith("Basic realm=");
        challenge.Header("DAV").Should().Contain("calendar-access");

        // 4. With credentials: root -> principal -> calendar home -> calendar.
        var root = await client.PropFindAsync("/caldav/", 0, Dav.D + "current-user-principal");
        root.Status.Should().Be((HttpStatusCode)207);
        var principalHref = root.Property("/caldav/", Dav.D + "current-user-principal")?.Value;
        principalHref.Should().Be(bridge.PrincipalPath);

        var principal = await client.PropFindAsync(principalHref!, 0, Dav.C + "calendar-home-set");
        principal.Status.Should().Be((HttpStatusCode)207);
        var homeHref = principal.Property(bridge.PrincipalPath, Dav.C + "calendar-home-set")?.Value;
        homeHref.Should().Be(bridge.HomePath);

        var home = await client.PropFindAsync(homeHref!, 1, Dav.CollectionProperties);
        home.Status.Should().Be((HttpStatusCode)207);
        home.Hrefs.Should().Equal(bridge.HomePath, bridge.CalendarPath());

        var calendar = bridge.CalendarPath();
        home.Property(calendar, Dav.D + "displayname")!.Value.Should().Be("DonetickTasks");
        home.Property(calendar, Dav.C + "supported-calendar-component-set")!
            .Element(Dav.C + "comp")!.Attribute("name")!.Value.Should().Be("VTODO");
        home.Property(calendar, Dav.CS + "getctag")!.Value.Should().NotBeNullOrEmpty();

        // 5. The calendar lists both chores from Donetick.
        var members = await client.PropFindAsync(calendar, 1, Dav.D + "getetag");
        members.Hrefs.Should().BeEquivalentTo(calendar, bridge.ResourcePath(1), bridge.ResourcePath(2));

        // The bridge sent the API key on every Donetick call (the stub only answers with the key).
        Donetick.AllRequests.Should().OnlyContain(r => r.Headers!["secretkey"].Contains(DonetickStub.ApiKey));
    }
}
