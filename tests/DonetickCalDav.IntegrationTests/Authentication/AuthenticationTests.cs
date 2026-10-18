using System.Net;
using System.Text;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Authentication;

/// <summary>IT-02: who gets in, who does not, and which endpoints stay anonymous.</summary>
[Trait("Scenario", "IT-02")]
[Trait("Covers", "FR-03")]
[Trait("Covers", "NFR-01")]
public class AuthenticationTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    public static TheoryData<string, string, string> RejectedAuthorizations => new()
    {
        { "wrong user", "Basic", Base64($"intruder:{BridgeHost.Password}") },
        { "wrong password", "Basic", Base64($"{BridgeHost.Username}:wrong-password") },
        { "user name in other case", "Basic", Base64($"{BridgeHost.Username.ToUpperInvariant()}:{BridgeHost.Password}") },
        { "password with extra character", "Basic", Base64($"{BridgeHost.Username}:{BridgeHost.Password}x") },
        { "malformed Base64", "Basic", "not-base64!!" },
        { "no colon", "Basic", Base64($"{BridgeHost.Username}{BridgeHost.Password}") },
        { "empty credentials", "Basic", Base64(":") },
        { "bearer token", "Bearer", "some-token" },
        { "digest", "Digest", "username=\"tester\"" },
    };

    [Theory(Timeout = DefaultTimeout)]
    [MemberData(nameof(RejectedAuthorizations))]
    public async Task Request_RejectedCredentials_Returns401WithBasicChallengeAndDav(string reason, string scheme, string parameter)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var client = bridge.CreateClient().WithAuthorization(scheme, parameter);

        var response = await client.PropFindAsync("/caldav/", 0, Dav.D + "current-user-principal");

        response.Status.Should().Be(HttpStatusCode.Unauthorized, reason);
        response.Header("WWW-Authenticate").Should().Be("Basic realm=\"DonetickCalDAV\"");
        response.Header("DAV").Should().Contain("calendar-access", "Apple only sends credentials to a recognised DAV server");
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("PROPFIND")]
    [InlineData("REPORT")]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PROPPATCH")]
    [InlineData("MOVE")]
    public async Task Request_NoCredentials_Returns401AndNeverReachesDonetick(string method)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1)).OnDelete(1).OnUpdate(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var requestsAtStart = Donetick.AllRequests.Count;
        var body = method == "PUT" ? Ics.VTodo(Ics.ChoreUid(1), "Changed") : null;

        var response = await bridge.CreateClient().Anonymous()
            .SendAsync(new HttpMethod(method), bridge.ResourcePath(1), body, method == "PUT" ? "text/calendar" : null);

        response.Status.Should().Be(HttpStatusCode.Unauthorized);
        Donetick.AllRequests.Should().HaveCount(requestsAtStart, "an unauthenticated request must not cause a Donetick call");
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("GET", "/")]
    [InlineData("GET", "/health")]
    [InlineData("GET", "/health/json")]
    [InlineData("PROPFIND", "/.well-known/caldav")]
    [InlineData("GET", "/.well-known/caldav")]
    [InlineData("OPTIONS", "/caldav/")]
    [InlineData("OPTIONS", "/caldav/calendars/tester/tasks/")]
    public async Task Request_AnonymousEndpoint_IsServedWithoutCredentials(string method, string path)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().Anonymous().SendAsync(new HttpMethod(method), path);

        response.Status.Should().NotBe(HttpStatusCode.Unauthorized);
        ((int)response.Status).Should().BeLessThan(400);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Request_ValidCredentials_IsServed()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().PropFindAsync("/caldav/", 0, Dav.D + "current-user-principal");

        response.Status.Should().Be((HttpStatusCode)207);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task Request_CustomCredentialsFromConfiguration_AreTheOnlyOnesAccepted()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync(o => { o.Username = "household"; o.Password = "p@ss:word with spaces"; });
        var client = bridge.CreateClient();

        (await client.PropFindAsync("/caldav/", 0)).Status.Should().Be((HttpStatusCode)207,
            "everything after the first colon is the password");
        (await client.WithCredentials(BridgeHost.Username, BridgeHost.Password).PropFindAsync("/caldav/", 0))
            .Status.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(Timeout = DefaultTimeout)]
    public async Task PropFind_PrincipalOfAnotherUser_Returns404()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var response = await bridge.CreateClient().PropFindAsync("/caldav/principals/someone-else/", 0);

        response.Status.Should().Be(HttpStatusCode.NotFound);
    }

    private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
}
