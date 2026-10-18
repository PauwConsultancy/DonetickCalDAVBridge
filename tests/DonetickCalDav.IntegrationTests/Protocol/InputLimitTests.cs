using System.Net;
using System.Text;
using DonetickCalDav.Testing;
using DonetickCalDav.Testing.Harness;
using FluentAssertions;

namespace DonetickCalDav.IntegrationTests.Protocol;

/// <summary>IT-11: request size limit and invalid bodies on PUT.</summary>
[Trait("Scenario", "IT-11")]
[Trait("Covers", "FR-19")]
[Trait("Covers", "NFR-11")]
public class InputLimitTests(DonetickStub donetick) : IntegrationTest(donetick)
{
    private const int Limit = 1024 * 1024;

    [Fact(Timeout = DefaultTimeout)]
    public async Task Put_ContentLengthOverOneMegabyte_Returns413WithoutCallingDonetick()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var requestsBefore = Donetick.AllRequests.Count;

        var response = await bridge.CreateClient().PutAsync(bridge.ResourcePath(1), OversizedIcs());

        response.Status.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        Donetick.AllRequests.Should().HaveCount(requestsBefore);
    }

    [Fact(Timeout = DefaultTimeout)]
    [Trait("KnownIssue", "F-28")]
    public async Task Put_ChunkedBodyOverOneMegabyte_FailsOnASynchronousReadInsteadOf413()
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();

        var put = () => bridge.CreateClient().SendAsync(HttpMethod.Put, bridge.ResourcePath(1), configure: request =>
        {
            // StreamContent of an unseekable stream has no Content-Length, like a chunked upload.
            request.Content = new StreamContent(new UnseekableStream(Encoding.UTF8.GetBytes(OversizedIcs())));
            request.Content.Headers.ContentType = new("text/calendar");
        });

        // StreamReader.Peek() reads synchronously, which Kestrel and TestServer forbid. TestServer
        // surfaces the exception; Kestrel answers 500 (confirmed against the published app).
        await put.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Synchronous operations are disallowed*");
    }

    [Theory(Timeout = DefaultTimeout)]
    [InlineData("this is not a calendar")]
    [InlineData("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:x\r\nBEGIN:VEVENT\r\nUID:e1\r\nSUMMARY:An event\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n")]
    [InlineData("")]
    public async Task Put_BodyWithoutVTodo_Returns400(string body)
    {
        Donetick.WithChores(TestChoreFactory.Simple(1));
        var bridge = await StartBridgeAsync();
        var requestsBefore = Donetick.AllRequests.Count;

        var response = await bridge.CreateClient().PutAsync(bridge.ResourcePath(1), body);

        response.Status.Should().Be(HttpStatusCode.BadRequest);
        Donetick.AllRequests.Should().HaveCount(requestsBefore);
    }

    private static string OversizedIcs() =>
        Ics.VTodo(Ics.ChoreUid(1), "Large", description: new string('x', Limit + 10));

    private sealed class UnseekableStream(byte[] data) : MemoryStream(data)
    {
        public override bool CanSeek => false;
    }
}
