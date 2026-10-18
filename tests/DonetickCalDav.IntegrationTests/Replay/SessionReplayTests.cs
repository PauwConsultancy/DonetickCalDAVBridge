using System.Text;
using DonetickCalDav.Testing.Harness;
using DonetickCalDav.Testing.Snapshots;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Replay;

/// <summary>
/// L3: replays recorded client sessions (tests/fixtures/sessions) through the integration harness.
/// Each request must get the status the client got when the session was recorded, the Donetick calls
/// must match the session's expectations, and the full transcript of responses is a snapshot.
/// </summary>
[Trait("Level", "Replay")]
public class SessionReplayTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    private static readonly string[] SkippedHeaders = ["Authorization", "Host", "Content-Length", "Content-Type"];
    private static readonly string[] SnapshotHeaders = ["Content-Type", "DAV", "ETag", "Location", "WWW-Authenticate", "Allow"];

    public static TheoryData<string> Sessions => new(ReplaySession.All());

    [Theory(Timeout = DefaultTimeout)]
    [MemberData(nameof(Sessions))]
    public async Task Replay_Session_MatchesRecording(string name)
    {
        var session = ReplaySession.Load(name);
        Donetick.WithReferenceData();
        var bridge = await StartBridgeAsync(o => o.GroupByLabel = session.Bridge.GroupByLabel);
        var client = bridge.CreateClient();
        var transcript = new StringBuilder($"# {session.Id} {session.Title} ({session.Source}: {session.Client})\n\n");

        for (var i = 0; i < session.Exchanges.Count; i++)
        {
            var exchange = session.Exchanges[i];
            var request = exchange.Request;
            var path = Expand(request.Path, bridge);
            var sender = request.Authenticated ? client : client.Anonymous();
            request.Headers.TryGetValue("Content-Type", out var contentType);

            var response = await sender.SendAsync(new HttpMethod(request.Method), path,
                request.Body is null ? null : Expand(request.Body, bridge), contentType ?? "application/xml",
                configure: message =>
                {
                    foreach (var (header, value) in request.Headers.Where(h => !SkippedHeaders.Contains(h.Key, StringComparer.OrdinalIgnoreCase)))
                    {
                        message.Headers.Remove(header);
                        message.Headers.TryAddWithoutValidation(header, Expand(value, bridge));
                    }
                });

            ((int)response.Status).Should().Be(exchange.Response.Status,
                $"exchange {i + 1} ({request.Method} {request.Path}) got {exchange.Response.Status} when the session was recorded");
            transcript.Append($"## {i + 1} {request.Method} {request.Path}\n{response.ToSnapshot(SnapshotHeaders)}\n\n");
        }

        foreach (var expected in session.ExpectDonetick)
        {
            Donetick.Count(expected.Method, expected.Path).Should().Be(expected.Count,
                $"the session expects {expected.Count} x {expected.Method} {expected.Path} at Donetick");
        }

        Snapshot.Match(transcript.ToString(), "txt", discriminator: name.Replace('/', '.'), scrubbers: Scrubbers.Http);
    }

    [Fact]
    public void Sessions_AreAvailable()
    {
        ReplaySession.All().Should().NotBeEmpty("tests/fixtures/sessions holds at least the synthetic Apple sessions");
    }

    private static string Expand(string text, BridgeHost bridge) =>
        text.Replace(ReplaySession.UserPlaceholder, bridge.Options.Username);
}
